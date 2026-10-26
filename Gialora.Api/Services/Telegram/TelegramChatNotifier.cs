// Gialora.Api/Services/Telegram/TelegramChatNotifier.cs
using Gialora.Application.Services;
using Microsoft.Extensions.Options;

namespace Gialora.Api.Services.Telegram;

/// <summary>
/// User-ի chat հաղորդագրությունը → admin-ի Telegram։ Ձախողումը սխալ չէ user-ի համար
/// (հաղորդագրությունը արդեն պահված է), ուստի այստեղ ոչինչ չի նետվում։
/// </summary>
public class TelegramChatNotifier : IChatNotifier
{
    private readonly TelegramBotClient _bot;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramChatNotifier> _logger;

    public TelegramChatNotifier(TelegramBotClient bot, IOptions<TelegramOptions> options, ILogger<TelegramChatNotifier> logger)
    {
        _bot = bot;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<long?> NotifyAdminAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogWarning("Telegram is not configured (Telegram:BotToken / Telegram:AdminChatId); chat message not relayed.");
            return null;
        }

        try
        {
            return await _bot.SendMessageAsync(_options.AdminChatId!.Value, text, cancellationToken);
        }
        // HttpClient-ի timeout-ը TaskCanceledException է (OperationCanceledException) —
        // type-ով filter-ը այն բաց էր թողնում → 500, իսկ message-ը արդեն պահված էր
        // (retry → կրկնակի հաղորդագրություն)։ Չեղարկում է միայն request-ի token-ը։
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // ex.Message-ը token չի պարունակում (TelegramApiException-ը միայն description-ն է պահում)
            _logger.LogError("Could not relay chat message to Telegram: {Error}", ex.Message);
            return null;
        }
    }
}
