// Gialora.Api/Services/Telegram/TelegramBotClient.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gialora.Api.Services.Telegram;

/// <summary>
/// "Telegram" section-ը։ BotToken-ը ԳԱՂՏՆԻ է — երբեք appsettings.json-ում կամ git-ում.
/// dev-ում՝ `dotnet user-secrets`, production-ում՝ environment variable
/// (Telegram__BotToken, Telegram__AdminChatId)։
/// </summary>
public class TelegramOptions
{
    public const string Section = "Telegram";

    public string? BotToken { get; set; }

    /// <summary>Ո՞ւր գնան user-ների հաղորդագրությունները — admin-ի անձնական chat-ը bot-ի հետ կամ խումբ։</summary>
    public long? AdminChatId { get; set; }

    /// <summary>Փոխվում է միայն թեստերում (fake Telegram server)։</summary>
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken) && AdminChatId is not null;
}

/// <summary>Telegram-ը պատասխանեց սխալով (description-ը token չի պարունակում)։</summary>
public class TelegramApiException : Exception
{
    public int? ErrorCode { get; }

    public TelegramApiException(int? errorCode, string? description)
        : base($"Telegram API error {errorCode}: {description}")
    {
        ErrorCode = errorCode;
    }

    /// <summary>409 — մեկ ուրիշը արդեն կարդում է update-ները (երկրորդ instance կամ webhook)։</summary>
    public bool IsConflict => ErrorCode == (int)HttpStatusCode.Conflict;
}

/// <summary>
/// Bot API-ի այն երկու մեթոդը, որ մեզ պետք է։ Սեփական փոքր client — ամբողջ SDK-ն
/// երկու կանչի համար ավելորդ կախվածություն կլիներ։
///
/// ՈՒՇԱԴՐՈՒԹՅՈՒՆ. Bot API-ի URL-ը ՊԱՐՈՒՆԱԿՈՒՄ Է token-ը (/bot&lt;token&gt;/method), ուստի
/// այս HttpClient-ի logging-ը անջատված է (Program.cs-ում RemoveAllLoggers) — այլապես
/// IHttpClientFactory-ն ամեն request-ի URL-ը Information մակարդակով կգրեր log-ում։
/// </summary>
public class TelegramBotClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly TelegramOptions _options;

    public TelegramBotClient(HttpClient http, Microsoft.Extensions.Options.IOptions<TelegramOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>Plain text (առանց parse_mode) — user-ի տեքստը Markdown/HTML չի մեկնաբանվի։</summary>
    public async Task<long> SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        var message = await CallAsync<TgMessage>("sendMessage", new
        {
            chat_id = chatId,
            text = text.Length > 4096 ? text[..4093] + "…" : text
        }, cancellationToken);

        return message.MessageId;
    }

    /// <summary>Long polling — Telegram-ը պահում է կապը մինչև <paramref name="timeoutSeconds"/>, եթե update չկա։</summary>
    public Task<List<TgUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken) =>
        CallAsync<List<TgUpdate>>("getUpdates", new
        {
            offset,
            timeout = timeoutSeconds,
            allowed_updates = new[] { "message" }
        }, cancellationToken);

    private async Task<T> CallAsync<T>(string method, object body, CancellationToken cancellationToken)
    {
        var url = $"{_options.ApiBaseUrl.TrimEnd('/')}/bot{_options.BotToken}/{method}";

        using var response = await _http.PostAsJsonAsync(url, body, Json, cancellationToken);

        // Telegram-ը սխալի դեպքում էլ JSON է վերադարձնում ({ ok: false, description })
        var payload = await response.Content.ReadFromJsonAsync<TgResponse<T>>(Json, cancellationToken);

        if (payload is not { Ok: true, Result: not null })
            throw new TelegramApiException(payload?.ErrorCode ?? (int)response.StatusCode, payload?.Description);

        return payload.Result;
    }
}

// --- Bot API-ի մոդելները (միայն մեզ պետք դաշտերը) ---

public class TgResponse<T>
{
    public bool Ok { get; set; }
    public T? Result { get; set; }
    public int? ErrorCode { get; set; }
    public string? Description { get; set; }
}

public class TgUpdate
{
    public long UpdateId { get; set; }
    public TgMessage? Message { get; set; }
}

public class TgMessage
{
    public long MessageId { get; set; }
    public TgChat Chat { get; set; } = new();
    public string? Text { get; set; }
    public TgMessage? ReplyToMessage { get; set; }
}

public class TgChat
{
    public long Id { get; set; }
}
