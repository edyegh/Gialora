using System.Text.RegularExpressions;
using Gialora.Application.Common;
using Gialora.Application.Services;
using Gialora.Data;
using Gialora.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gialora.Tests;

/// <summary>
/// Registration creates an unconfirmed account and emails a 6-digit code; only the code
/// activates it. Until then login is refused (after the password check, so it reveals
/// nothing to someone without the password), and codes are short-lived and attempt-limited.
/// </summary>
public class EmailVerificationTests
{
    private const string Email = "ana@example.com";
    private const string Password = "correct-horse-1";

    /// <summary>Records sent emails instead of talking to SMTP.</summary>
    private sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public bool Fail { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new InvalidOperationException("SMTP down");

            Sent.Add(message);
            return Task.CompletedTask;
        }

        public string LastCode => Regex.Match(Sent[^1].TextBody, @"\b\d{6}\b").Value;
    }

    private readonly GialoraDbContext _db = new(new DbContextOptionsBuilder<GialoraDbContext>()
        .UseInMemoryDatabase($"verify-{Guid.NewGuid()}")
        .Options);

    private readonly FakeEmailSender _email = new();

    private AuthService Service() => new(_db, _email, NullLogger<AuthService>.Instance);

    private Task<RegisterResultDto> RegisterAsync(string password = Password, string? language = null) =>
        Service().RegisterAsync(new RegisterDto { Email = "  Ana@Example.com ", Password = password, DisplayName = "Ana", Language = language });

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    [Fact]
    public async Task Registering_sends_a_code_and_leaves_the_account_unconfirmed()
    {
        var result = await RegisterAsync();

        Assert.Equal(Email, result.Email);
        var user = await _db.Users.SingleAsync();
        Assert.False(user.EmailConfirmed);

        var sent = Assert.Single(_email.Sent);
        Assert.Equal(Email, sent.To);
        Assert.Matches(@"^\d{6}$", _email.LastCode);

        // Only the hash is stored, never the code itself
        Assert.NotNull(user.EmailVerificationCodeHash);
        Assert.DoesNotContain(_email.LastCode, user.EmailVerificationCodeHash);
    }

    [Fact]
    public async Task The_email_is_written_in_the_requested_language()
    {
        await RegisterAsync(language: "hy");

        Assert.Contains("հաստատման", _email.Sent[^1].Subject);
    }

    [Fact]
    public async Task Login_is_refused_until_the_email_is_confirmed()
    {
        await RegisterAsync();

        var ex = await Assert.ThrowsAsync<EmailNotConfirmedException>(() =>
            Service().ValidateCredentialsAsync(new LoginDto { Email = Email, Password = Password }));
        Assert.Equal(Email, ex.Email);
    }

    [Fact]
    public async Task A_wrong_password_on_an_unconfirmed_account_reveals_nothing()
    {
        await RegisterAsync();

        var result = await Service().ValidateCredentialsAsync(new LoginDto { Email = Email, Password = "wrong-password" });

        Assert.Null(result);
    }

    [Fact]
    public async Task The_right_code_confirms_the_account_and_allows_login()
    {
        await RegisterAsync();

        var verified = await Service().VerifyEmailAsync(new VerifyEmailDto { Email = Email, Code = $" {_email.LastCode} " });
        Assert.Equal(Email, verified.Email);

        var user = await _db.Users.SingleAsync();
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.EmailVerificationCodeHash);

        var login = await Service().ValidateCredentialsAsync(new LoginDto { Email = Email, Password = Password });
        Assert.NotNull(login);
    }

    [Fact]
    public async Task A_code_cannot_be_used_twice()
    {
        await RegisterAsync();
        var code = _email.LastCode;
        await Service().VerifyEmailAsync(new VerifyEmailDto { Email = Email, Code = code });

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Service().VerifyEmailAsync(new VerifyEmailDto { Email = Email, Code = code }));
    }

    [Fact]
    public async Task After_too_many_wrong_codes_even_the_right_one_is_rejected()
    {
        await RegisterAsync();
        var code = _email.LastCode;

        for (var i = 0; i < AuthService.MaxVerificationAttempts; i++)
        {
            await Assert.ThrowsAsync<ValidationFailedException>(() =>
                Service().VerifyEmailAsync(new VerifyEmailDto { Email = Email, Code = WrongCode(code) }));
        }

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Service().VerifyEmailAsync(new VerifyEmailDto { Email = Email, Code = code }));
        Assert.False((await _db.Users.SingleAsync()).EmailConfirmed);
    }

    [Fact]
    public async Task An_expired_code_is_rejected()
    {
        await RegisterAsync();
        var user = await _db.Users.SingleAsync();
        user.EmailVerificationExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Service().VerifyEmailAsync(new VerifyEmailDto { Email = Email, Code = _email.LastCode }));
    }

    [Fact]
    public async Task Resend_respects_the_cooldown_then_issues_a_new_code()
    {
        await RegisterAsync();

        await Service().ResendVerificationAsync(new ResendVerificationDto { Email = Email });
        Assert.Single(_email.Sent); // still inside the cooldown

        var user = await _db.Users.SingleAsync();
        user.EmailVerificationSentUtc = DateTime.UtcNow - AuthService.ResendCooldown - TimeSpan.FromSeconds(1);
        await _db.SaveChangesAsync();

        await Service().ResendVerificationAsync(new ResendVerificationDto { Email = Email });
        Assert.Equal(2, _email.Sent.Count);

        await Service().VerifyEmailAsync(new VerifyEmailDto { Email = Email, Code = _email.LastCode });
        Assert.True((await _db.Users.SingleAsync()).EmailConfirmed);
    }

    [Fact]
    public async Task Resend_is_silent_for_unknown_emails()
    {
        await Service().ResendVerificationAsync(new ResendVerificationDto { Email = "nobody@example.com" });

        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Registering_again_before_confirming_replaces_the_pending_account()
    {
        await RegisterAsync(password: "first-password-1");
        await RegisterAsync(password: Password);

        Assert.Single(await _db.Users.ToListAsync());
        Assert.Equal(2, _email.Sent.Count);

        await Service().VerifyEmailAsync(new VerifyEmailDto { Email = Email, Code = _email.LastCode });
        Assert.NotNull(await Service().ValidateCredentialsAsync(new LoginDto { Email = Email, Password = Password }));
    }

    [Fact]
    public async Task Registering_a_confirmed_email_is_a_conflict()
    {
        await RegisterAsync();
        await Service().VerifyEmailAsync(new VerifyEmailDto { Email = Email, Code = _email.LastCode });

        await Assert.ThrowsAsync<ConflictException>(() => RegisterAsync());
    }

    [Fact]
    public async Task A_failed_email_send_is_reported_but_the_account_can_retry()
    {
        _email.Fail = true;
        await Assert.ThrowsAsync<EmailDeliveryException>(() => RegisterAsync());

        _email.Fail = false;
        await RegisterAsync();
        await Service().VerifyEmailAsync(new VerifyEmailDto { Email = Email, Code = _email.LastCode });
        Assert.True((await _db.Users.SingleAsync()).EmailConfirmed);
    }
}
