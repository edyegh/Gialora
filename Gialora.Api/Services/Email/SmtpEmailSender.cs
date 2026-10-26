// Gialora.Api/Services/Email/SmtpEmailSender.cs
using Gialora.Application.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Gialora.Api.Services.Email;

/// <summary>
/// "Smtp" section։ Password-ը ԳԱՂՏՆԻ է՝ user-secrets / environment variable
/// (Smtp__Password), երբեք appsettings.json-ում։
/// </summary>
public class SmtpOptions
{
    public const string Section = "Smtp";

    public string? Host { get; set; }

    /// <summary>587 — STARTTLS (լռելյայն), 465 — անմիջական TLS։</summary>
    public int Port { get; set; } = 587;

    public string? Username { get; set; }
    public string? Password { get; set; }

    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Gialora";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress!));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        // 465 → SslOnConnect, մնացածը → STARTTLS։ Գաղտնագրումը պարտադիր է —
        // password-ը և կոդը բաց տեքստով ցանցով չպիտի գնան։
        var security = _options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        using var client = new SmtpClient { Timeout = 15_000 };
        await client.ConnectAsync(_options.Host!, _options.Port, security, cancellationToken);

        if (!string.IsNullOrEmpty(_options.Username))
            await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken);

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}

/// <summary>
/// ՄԻԱՅՆ Development, երբ SMTP-ն կարգավորված չէ — email-ը (կոդով) գրում է log-ում,
/// որ local-ում գրանցումը փորձարկվի առանց mail server-ի։ Production-ում չի գրանցվում։
/// </summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("SMTP is not configured (dev only) — email to {To} not sent. Subject: {Subject}\n{Body}",
            message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}
