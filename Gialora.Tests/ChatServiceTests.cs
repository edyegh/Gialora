// Gialora.Tests/ChatServiceTests.cs
using Gialora.Application.Common;
using Gialora.Application.Services;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gialora.Tests;

/// <summary>
/// Support chat: a visitor's message is stored and relayed to the admin's Telegram;
/// the admin answers with Telegram's "Reply", which is routed back to the right
/// conversation by the relayed message's id. Guests are identified only by a secret
/// key held in their browser, so one guest must never see another's conversation.
/// </summary>
public class ChatServiceTests
{
    private const string GuestKey = "guest-key-0123456789abcdefghijklmnopqrstuv";
    private const string OtherGuestKey = "other-key-0123456789abcdefghijklmnopqrstuv";

    /// <summary>Records what would have been sent to Telegram and hands out message ids.</summary>
    private sealed class FakeNotifier : IChatNotifier
    {
        public bool Configured { get; set; } = true;
        public List<string> Sent { get; } = new();
        private long _nextId = 1000;

        public Task<long?> NotifyAdminAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!Configured)
                return Task.FromResult<long?>(null);

            Sent.Add(text);
            return Task.FromResult<long?>(_nextId++);
        }
    }

    private readonly GialoraDbContext _db = new(new DbContextOptionsBuilder<GialoraDbContext>()
        .UseInMemoryDatabase($"chat-{Guid.NewGuid()}")
        .Options);

    private readonly FakeNotifier _telegram = new();

    private ChatService Service() => new(_db, _telegram, NullLogger<ChatService>.Instance);

    private static ChatCaller Guest(string key = GuestKey) => new(null, key);

    private long LastRelayedTelegramId() =>
        _db.ChatMessages.Where(m => m.Sender == ChatSender.User).OrderByDescending(m => m.CreatedAtUtc).First().TelegramMessageId!.Value;

    [Fact]
    public async Task A_guest_message_is_saved_and_relayed_to_telegram()
    {
        var sent = await Service().SendAsync(Guest(), new ChatSendDto { Text = "  Do you have vegan recipes?  " });

        Assert.Equal("Do you have vegan recipes?", sent.Text);
        Assert.Equal(ChatSender.User, sent.Sender);

        var relayed = Assert.Single(_telegram.Sent);
        Assert.Contains("Do you have vegan recipes?", relayed);
        Assert.Contains("Guest ", relayed);
        Assert.Contains("new conversation", relayed);

        Assert.Equal(1000, LastRelayedTelegramId());
    }

    [Fact]
    public async Task Only_the_first_message_is_flagged_as_a_new_conversation()
    {
        await Service().SendAsync(Guest(), new ChatSendDto { Text = "Hi" });
        await Service().SendAsync(Guest(), new ChatSendDto { Text = "Are you there?" });

        Assert.Contains("new conversation", _telegram.Sent[0]);
        Assert.DoesNotContain("new conversation", _telegram.Sent[1]);
        Assert.Single(_db.ChatConversations);
    }

    [Fact]
    public async Task An_admin_reply_in_telegram_reaches_the_right_guest()
    {
        await Service().SendAsync(Guest(), new ChatSendDto { Text = "Hi" });
        await Service().SendAsync(Guest(OtherGuestKey), new ChatSendDto { Text = "Hello from someone else" });

        var firstGuestTelegramId = _db.ChatMessages.Single(m => m.Text == "Hi").TelegramMessageId;

        var outcome = await Service().HandleAdminMessageAsync(5001, firstGuestTelegramId, "Hello! How can we help?");

        Assert.Equal(AdminReplyOutcome.Delivered, outcome);

        var mine = await Service().GetMessagesAsync(Guest());
        Assert.Equal(new[] { "Hi", "Hello! How can we help?" }, mine.Select(m => m.Text));
        Assert.Equal(ChatSender.Admin, mine[1].Sender);

        var theirs = await Service().GetMessagesAsync(Guest(OtherGuestKey));
        Assert.Equal(new[] { "Hello from someone else" }, theirs.Select(m => m.Text));
    }

    [Fact]
    public async Task Replying_to_the_admins_own_earlier_answer_stays_in_the_same_conversation()
    {
        await Service().SendAsync(Guest(), new ChatSendDto { Text = "Hi" });
        await Service().HandleAdminMessageAsync(5001, LastRelayedTelegramId(), "First answer");

        var outcome = await Service().HandleAdminMessageAsync(5002, 5001, "Follow-up");

        Assert.Equal(AdminReplyOutcome.Delivered, outcome);
        Assert.Equal(3, (await Service().GetMessagesAsync(Guest())).Count);
    }

    [Fact]
    public async Task The_same_telegram_update_is_only_delivered_once()
    {
        await Service().SendAsync(Guest(), new ChatSendDto { Text = "Hi" });
        var relayed = LastRelayedTelegramId();

        await Service().HandleAdminMessageAsync(5001, relayed, "Answer");
        var again = await Service().HandleAdminMessageAsync(5001, relayed, "Answer");

        Assert.Equal(AdminReplyOutcome.AlreadyDelivered, again);
        Assert.Single(_db.ChatMessages, m => m.Sender == ChatSender.Admin);
    }

    [Fact]
    public async Task Admin_messages_that_are_not_replies_or_unknown_are_reported()
    {
        Assert.Equal(AdminReplyOutcome.NotAReply, await Service().HandleAdminMessageAsync(1, null, "hello?"));
        Assert.Equal(AdminReplyOutcome.ConversationNotFound, await Service().HandleAdminMessageAsync(2, 424242, "hello?"));
        Assert.Equal(AdminReplyOutcome.Empty, await Service().HandleAdminMessageAsync(3, 1000, "   "));
        Assert.Empty(_db.ChatMessages);
    }

    [Fact]
    public async Task A_guest_without_a_conversation_sees_nothing()
    {
        await Service().SendAsync(Guest(), new ChatSendDto { Text = "Hi" });

        Assert.Empty(await Service().GetMessagesAsync(Guest(OtherGuestKey)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too-short")]
    [InlineData("has spaces in it which are not allowed at all!!")]
    public async Task A_missing_or_weak_guest_key_is_rejected(string? key)
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Service().SendAsync(new ChatCaller(null, key), new ChatSendDto { Text = "Hi" }));
    }

    [Fact]
    public async Task Messages_are_kept_even_when_telegram_is_not_configured()
    {
        _telegram.Configured = false;

        await Service().SendAsync(Guest(), new ChatSendDto { Text = "Hi" });

        var stored = Assert.Single(_db.ChatMessages);
        Assert.Null(stored.TelegramMessageId);
    }

    [Fact]
    public async Task A_signed_in_user_is_identified_by_account_not_by_guest_key()
    {
        var user = new User { Email = "ana@example.com", DisplayName = "Ana", PasswordHash = "x" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Same browser key as an earlier guest conversation, but signed in now
        await Service().SendAsync(Guest(), new ChatSendDto { Text = "As a guest" });
        await Service().SendAsync(new ChatCaller(user.Id, GuestKey), new ChatSendDto { Text = "As Ana" });

        Assert.Contains("Ana <ana@example.com>", _telegram.Sent[1]);
        Assert.Equal(new[] { "As Ana" }, (await Service().GetMessagesAsync(new ChatCaller(user.Id, null))).Select(m => m.Text));
        Assert.Equal(2, _db.ChatConversations.Count());
    }

    [Fact]
    public async Task The_guest_key_itself_is_never_stored()
    {
        await Service().SendAsync(Guest(), new ChatSendDto { Text = "Hi" });

        var conversation = Assert.Single(_db.ChatConversations);
        Assert.NotEqual(GuestKey, conversation.GuestKeyHash);
        Assert.Equal(ChatService.HashGuestKey(GuestKey), conversation.GuestKeyHash);
    }
}
