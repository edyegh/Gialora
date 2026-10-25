// Gialora.Api/Services/Telegram/TelegramPollingService.cs
using Gialora.Application.Services;
using Microsoft.Extensions.Options;

namespace Gialora.Api.Services.Telegram;

/// <summary>
/// Admin-ի պատասխանները Telegram-ից → user-ի chat։
///
/// Long polling (getUpdates), ոչ թե webhook. աշխատում է localhost-ում էլ և չի պահանջում
/// հանրային HTTPS հասցե։ Սահմանափակումը՝ միաժամանակ ՄԵԿ instance կարող է կարդալ
/// (երկրորդը Telegram-ից 409 է ստանում) — մի քանի server-ի դեպքում պետք կլինի webhook։
///
/// Անվտանգություն. ընդունվում են ՄԻԱՅՆ AdminChatId-ից եկած հաղորդագրությունները —
/// bot-ին ցանկացած մեկը կարող է գրել, և նրա "պատասխանը" չպիտի հասնի user-ներին։
/// </summary>
public class TelegramPollingService : BackgroundService
{
    private const int LongPollSeconds = 50;

    private readonly TelegramBotClient _bot;
    private readonly TelegramOptions _options;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TelegramPollingService> _logger;

    public TelegramPollingService(
        TelegramBotClient bot,
        IOptions<TelegramOptions> options,
        IServiceScopeFactory scopes,
        ILogger<TelegramPollingService> logger)
    {
        _bot = bot;
        _options = options.Value;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogInformation("Telegram is not configured — admin replies from Telegram are disabled.");
            return;
        }

        _logger.LogInformation("Listening for admin replies from Telegram");
        long offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _bot.GetUpdatesAsync(offset, LongPollSeconds, stoppingToken);

                foreach (var update in updates)
                {
                    // Հաջորդ getUpdates-ը offset-ով "հաստատում" է այս update-ը Telegram-ում
                    offset = Math.Max(offset, update.UpdateId + 1);
                    await HandleAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (TelegramApiException ex) when (ex.IsConflict)
            {
                _logger.LogError("Telegram returned 409: another instance is polling this bot, or a webhook is set. Retrying in 30 s.");
                await Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                // Ցանցի խափանում, Telegram-ի 5xx — չենք կանգնում, փորձում ենք նորից
                _logger.LogWarning("Telegram polling failed: {Error}. Retrying in 5 s.", ex.Message);
                await Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(TgUpdate update, CancellationToken stoppingToken)
    {
        var message = update.Message;
        if (message is null)
            return;

        if (message.Chat.Id != _options.AdminChatId)
        {
            _logger.LogInformation("Ignored a Telegram message from chat {ChatId} (not the admin chat)", message.Chat.Id);
            return;
        }

        // ChatService-ը scoped է (DbContext), իսկ BackgroundService-ը՝ singleton
        await using var scope = _scopes.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();

        var outcome = await chat.HandleAdminMessageAsync(
            message.MessageId, message.ReplyToMessage?.MessageId, message.Text);

        // Admin-ին բացատրում ենք միայն այն, ինչ նա պիտի ուղղի — հաջողությունը լուռ է
        var hint = outcome switch
        {
            AdminReplyOutcome.NotAReply =>
                "↩️ To answer a user, swipe left on (or long-press → Reply to) their message, then send your answer.",
            AdminReplyOutcome.ConversationNotFound =>
                "⚠️ That message isn't linked to a Gialora chat. Reply directly to a user's message.",
            _ => null
        };

        if (hint is not null)
        {
            try
            {
                await _bot.SendMessageAsync(message.Chat.Id, hint, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Could not send a hint to the Telegram admin chat: {Error}", ex.Message);
            }
        }
    }

    private static async Task Delay(TimeSpan delay, CancellationToken token)
    {
        try { await Task.Delay(delay, token); }
        catch (OperationCanceledException) { }
    }
}
