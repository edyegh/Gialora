// Gialora.Application/Services/ChatService.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;

namespace Gialora.Application.Services;

/// <summary>
/// Ով է գրում։ Մուտք գործածը՝ UserId-ով, հյուրը՝ browser-ում պահված գաղտնի բանալիով։
/// Եթե երկուսն էլ կան, UserId-ն է հաղթում։
/// </summary>
public record ChatCaller(Guid? UserId, string? GuestKey);

/// <summary>
/// Admin-ին հաղորդագրություն հասցնելու ալիքը (Telegram)։ Application-ը չգիտի, թե
/// ինչպես — իրականացումը Api-ում է։ Վերադարձնում է ուղարկված հաղորդագրության
/// id-ն, որի վրա admin-ը "Reply" կանի, կամ null, եթե ալիքը կարգավորված չէ / ձախողվեց։
/// </summary>
public interface IChatNotifier
{
    Task<long?> NotifyAdminAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>Admin-ի Telegram հաղորդագրության մշակման արդյունքը։</summary>
public enum AdminReplyOutcome
{
    Delivered,
    AlreadyDelivered,   // նույն update-ը կրկին (restart-ից հետո)
    NotAReply,          // admin-ը գրեց, բայց ոչ մի հաղորդագրության "Reply" չարեց
    ConversationNotFound,
    Empty
}

public interface IChatService
{
    Task<List<ChatMessageDto>> GetMessagesAsync(ChatCaller caller);
    Task<ChatMessageDto> SendAsync(ChatCaller caller, ChatSendDto dto, CancellationToken cancellationToken = default);

    /// <summary>Admin-ը Telegram-ում պատասխանեց — հասցնում ենք user-ին։</summary>
    Task<AdminReplyOutcome> HandleAdminMessageAsync(
        long adminTelegramMessageId, long? repliedToTelegramMessageId, string? text);
}

public partial class ChatService : IChatService
{
    private const int MaxMessagesReturned = 200;

    private readonly GialoraDbContext _db;
    private readonly IChatNotifier _notifier;
    private readonly ILogger<ChatService> _logger;

    public ChatService(GialoraDbContext db, IChatNotifier notifier, ILogger<ChatService> logger)
    {
        _db = db;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<List<ChatMessageDto>> GetMessagesAsync(ChatCaller caller)
    {
        var conversation = await FindConversationAsync(caller);
        if (conversation is null)
            return new List<ChatMessageDto>();

        // Վերջին 200-ը, ժամանակագրական կարգով։ Ամբողջն ենք վերադարձնում, ոչ թե "X-ից հետո" —
        // ժամանակի համեմատությունը JSON-ով (DateTime.Kind) client'ի և server'ի միջև փխրուն է,
        // իսկ chat-ի չափերի դեպքում ամբողջը կարդալը էժան է։
        var messages = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(MaxMessagesReturned)
            .Select(m => new ChatMessageDto
            {
                Id = m.Id,
                Sender = m.Sender,
                Text = m.Text,
                SentAtUtc = DateTime.SpecifyKind(m.CreatedAtUtc, DateTimeKind.Utc) // SQL-ից Kind-ը Unspecified է գալիս
            })
            .ToListAsync();

        messages.Reverse();
        return messages;
    }

    public async Task<ChatMessageDto> SendAsync(ChatCaller caller, ChatSendDto dto, CancellationToken cancellationToken = default)
    {
        var text = dto.Text.Trim();
        if (text.Length == 0)
            throw new ValidationFailedException("Write a message first.");

        var conversation = await FindConversationAsync(caller, tracking: true);
        var isNew = conversation is null;

        if (conversation is null)
        {
            conversation = new ChatConversation
            {
                UserId = caller.UserId,
                GuestKeyHash = caller.UserId is null ? HashGuestKey(RequireGuestKey(caller)) : null
            };
            _db.ChatConversations.Add(conversation);
        }

        conversation.LastMessageAtUtc = DateTime.UtcNow;

        // FK-ն բացահայտ ենք դնում և DbSet.Add — ոչ թե conversation.Messages.Add
        // (BaseEntity-ի Id-ի պատճառով tracked parent-ի collection-ը UPDATE կտար, տե՛ս CLAUDE.md)
        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            Sender = ChatSender.User,
            Text = text
        };
        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);

        // Telegram-ը կարող է ձախողվել կամ կարգավորված չլինել — հաղորդագրությունը միևնույն է
        // պահված է, ուստի user-ի համար սա սխալ չէ։
        var who = await DescribeAsync(conversation, cancellationToken);
        var telegramText =
            $"💬 {who}{(isNew ? " · new conversation" : string.Empty)}\n\n{text}\n\n↩️ Reply to this message to answer.";

        var telegramId = await _notifier.NotifyAdminAsync(telegramText, cancellationToken);
        if (telegramId is not null)
        {
            message.TelegramMessageId = telegramId;
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            _logger.LogWarning("Chat message {MessageId} was saved but not delivered to Telegram", message.Id);
        }

        return Map(message);
    }

    public async Task<AdminReplyOutcome> HandleAdminMessageAsync(
        long adminTelegramMessageId, long? repliedToTelegramMessageId, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return AdminReplyOutcome.Empty;

        if (repliedToTelegramMessageId is null)
            return AdminReplyOutcome.NotAReply;

        // Նույն update-ը երկրորդ անգամ (offset-ը չհաստատվեց մինչև restart-ը)
        if (await _db.ChatMessages.AnyAsync(m =>
                m.Sender == ChatSender.Admin && m.TelegramMessageId == adminTelegramMessageId))
            return AdminReplyOutcome.AlreadyDelivered;

        // Admin-ը կարող է "Reply" անել ԻՐ նախորդ պատասխանին էլ — երկուսն էլ նույն խոսակցությունն են
        var conversationId = await _db.ChatMessages
            .Where(m => m.TelegramMessageId == repliedToTelegramMessageId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => (Guid?)m.ConversationId)
            .FirstOrDefaultAsync();

        if (conversationId is null)
            return AdminReplyOutcome.ConversationNotFound;

        var conversation = await _db.ChatConversations.FirstAsync(c => c.Id == conversationId);
        conversation.LastMessageAtUtc = DateTime.UtcNow;

        _db.ChatMessages.Add(new ChatMessage
        {
            ConversationId = conversation.Id,
            Sender = ChatSender.Admin,
            Text = text.Trim(),
            TelegramMessageId = adminTelegramMessageId
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation("Admin reply delivered to chat conversation {ConversationId}", conversation.Id);
        return AdminReplyOutcome.Delivered;
    }

    // -----------------------------------------------------------------------

    private async Task<ChatConversation?> FindConversationAsync(ChatCaller caller, bool tracking = false)
    {
        var query = tracking ? _db.ChatConversations : _db.ChatConversations.AsNoTracking();

        if (caller.UserId is { } userId)
            return await query.FirstOrDefaultAsync(c => c.UserId == userId);

        var hash = HashGuestKey(RequireGuestKey(caller));
        return await query.FirstOrDefaultAsync(c => c.GuestKeyHash == hash);
    }

    private static string RequireGuestKey(ChatCaller caller)
    {
        // Կարճ կամ կանխատեսելի բանալին թույլ կտար ուրիշի խոսակցությունը գուշակել
        if (caller.GuestKey is not { } key || !GuestKeyFormat().IsMatch(key))
            throw new ValidationFailedException("A chat key is required. Reload the page and try again.");

        return key;
    }

    public static string HashGuestKey(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    [GeneratedRegex("^[A-Za-z0-9_-]{32,128}$")]
    private static partial Regex GuestKeyFormat();

    /// <summary>Admin-ը Telegram-ում պիտի հասկանա՝ ում հետ է խոսում։</summary>
    private async Task<string> DescribeAsync(ChatConversation conversation, CancellationToken cancellationToken)
    {
        if (conversation.UserId is { } userId)
        {
            var user = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.DisplayName, u.Email })
                .FirstOrDefaultAsync(cancellationToken);

            if (user is not null)
                return $"{user.DisplayName} <{user.Email}>";
        }

        // Hash-ի սկիզբը կայուն է նույն browser-ի համար — admin-ը տարբերում է հյուրերին
        return $"Guest {conversation.GuestKeyHash?[..6]}";
    }

    private static ChatMessageDto Map(ChatMessage m) => new()
    {
        Id = m.Id,
        Sender = m.Sender,
        Text = m.Text,
        SentAtUtc = DateTime.SpecifyKind(m.CreatedAtUtc, DateTimeKind.Utc)
    };
}
