// Gialora.Application/Services/AuthService.cs
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Gialora.Application.Common;
using BCrypt.Net;
using Microsoft.Extensions.Logging;

namespace Gialora.Application.Services;

public class AuthService : IAuthService
{
    private readonly GialoraDbContext _db;
    private readonly IEmailSender _email;
    private readonly ILogger<AuthService> _logger;

    // Login-ի attempt-երի սահմանաչափը (brute-force պաշտպանություն)
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // Email-ի հաստատման կոդը. 6 նիշ = 1 000 000 տարբերակ, իսկ մեկ կոդի համար թույլ ենք
    // տալիս ընդամենը 5 փորձ — գուշակելու հավանականությունը 0.0005% է։ Դրանից հետո՝ նոր կոդ։
    public const int VerificationCodeValidMinutes = 15;
    public const int MaxVerificationAttempts = 5;
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    private const string InvalidCodeMessage = "The code is invalid or has expired. Please request a new one.";

    // Իրական bcrypt hash (workFactor 12) գոյություն չունեցող password-ի համար։
    // Օգտագործվում է միայն որպես "dummy" ստուգում, որ անհայտ email-ի response time-ը
    // նույնը լինի, ինչ գոյություն ունեցողինը (timing attack-ի կանխարգելում)։
    private const string DummyPasswordHash =
        "$2a$12$tQdjcE1edb/OxXUyiAa7lOXy/tRAGBcHmsG9ua2NvGawythMt7eQq";

    public AuthService(GialoraDbContext db, IEmailSender email, ILogger<AuthService> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    public async Task<RegisterResultDto> RegisterAsync(RegisterDto dto)
    {
        var normalizedEmail = NormalizeEmail(dto.Email);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user is { EmailConfirmed: true })
            throw new ConflictException("An account with this email already exists.");

        if (user is not null)
        {
            // Չհաստատված account — ոչ ոք դեռ չի ապացուցել, որ email-ը իրենն է։ Թույլ ենք
            // տալիս նորից գրանցվել (կորցրած կոդ, սխալ password), այլապես email-ը
            // ընդմիշտ "զբաղված" կմնար։ Account-ը կստանա նա, ով կոդը կմուտքագրի։
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 12);
            user.DisplayName = dto.DisplayName.Trim();
        }
        else
        {
            user = new User
            {
                Email = normalizedEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 12),
                DisplayName = dto.DisplayName.Trim(),
                Role = UserRole.User, // ամեն ինքնուրույն գրանցում — միշտ սովորական user, ոչ admin
                EmailConfirmed = false
            };
            _db.Users.Add(user);
        }

        var code = IssueVerificationCode(user);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Երկու հոգի միաժամանակ գրանցվեցին նույն email-ով — unique index-ը բռնեց։
            // Առանց սրա սա կվերածվեր 500-ի, փոխարենը՝ նույն 409-ը, ինչ վերևի ստուգումը։
            _db.Entry(user).State = EntityState.Detached;
            throw new ConflictException("An account with this email already exists.");
        }

        _logger.LogInformation("New user registered, awaiting email confirmation: {UserId}", user.Id);

        await SendVerificationEmailAsync(user, code, dto.Language);

        return new RegisterResultDto { Email = user.Email };
    }

    public async Task<AuthResultDto> VerifyEmailAsync(VerifyEmailDto dto)
    {
        var normalizedEmail = NormalizeEmail(dto.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        // Անհայտ email, արդեն հաստատված, կոդ չկա, ժամկետն անցել է — նույն պատասխանը
        if (user is null || user.EmailConfirmed || user.EmailVerificationCodeHash is null
            || user.EmailVerificationExpiresUtc is null || user.EmailVerificationExpiresUtc <= DateTime.UtcNow
            || user.EmailVerificationAttempts >= MaxVerificationAttempts)
        {
            throw new ValidationFailedException(InvalidCodeMessage);
        }

        var expected = Convert.FromHexString(user.EmailVerificationCodeHash);
        var actual = HashVerificationCode(dto.Code.Trim());

        // Constant-time համեմատություն — timing-ով կոդը նիշ առ նիշ գուշակել չի լինի
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            user.EmailVerificationAttempts++;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Wrong verification code for {UserId} (attempt {Attempt})", user.Id, user.EmailVerificationAttempts);
            throw new ValidationFailedException(InvalidCodeMessage);
        }

        user.EmailConfirmed = true;
        ClearVerificationCode(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Email confirmed: {UserId}", user.Id);

        return ToDto(user);
    }

    public async Task ResendVerificationAsync(ResendVerificationDto dto)
    {
        var normalizedEmail = NormalizeEmail(dto.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        // Լուռ ենք մնում — պատասխանից չպիտի երևա՝ այդ email-ով account կա՞, թե՞ ոչ
        if (user is null || user.EmailConfirmed)
            return;

        await ResendIfCooledDownAsync(user, dto.Language);
    }

    public async Task<AuthResultDto?> ValidateCredentialsAsync(LoginDto dto)
    {
        var normalizedEmail = NormalizeEmail(dto.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        // Կարևոր. նույն error-ի ուղին user-ի գոյության ստուգման ու սխալ password-ի համար
        // (կանխում ենք "email enumeration" — attacker-ը չպիտի կարողանա գուշակել, թե որ email-երն են գրանցված)
        if (user is null)
        {
            // Կատարում ենք dummy hash-check, որ response time-ը նույնը մնա (timing attack-ի կանխարգելում)
            BCrypt.Net.BCrypt.Verify(dto.Password, DummyPasswordHash);
            return null;
        }

        // Ստուգիր՝ account-ը lockout-ի մեջ չէ՞
        if (user.LockoutEndUtc is not null && user.LockoutEndUtc > DateTime.UtcNow)
        {
            _logger.LogWarning("Login attempt on locked account: {UserId}", user.Id);
            return null;
        }

        // Lockout-ը լրացել է — զրոյացնում ենք counter-ը, այլապես հաջորդ ՄԵԿ սխալ փորձը
        // անմիջապես նորից կկողպեր account-ը (5-ի հասած counter-ը երբեք չէր reset լինում)։
        if (user.LockoutEndUtc is not null)
        {
            user.LockoutEndUtc = null;
            user.FailedLoginAttempts = 0;
        }

        var isValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);

        if (!isValid)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutEndUtc = DateTime.UtcNow.Add(LockoutDuration);
                _logger.LogWarning("Account locked due to repeated failed logins: {UserId}", user.Id);
            }
            await _db.SaveChangesAsync();
            return null;
        }

        // Հաջող login — reset counters
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        await _db.SaveChangesAsync();

        if (!user.EmailConfirmed)
        {
            // Password-ը ճիշտ է, բայց email-ը չհաստատված — token չենք տալիս։ Նոր կոդ ենք
            // ուղարկում (եթե cooldown-ն անցել է), որ verify էջում user-ը անմիջապես ունենա այն։
            await ResendIfCooledDownAsync(user, dto.Language);
            throw new EmailNotConfirmedException(user.Email);
        }

        return ToDto(user);
    }

    public async Task<AuthResultDto?> GetByIdAsync(Guid userId)
    {
        // Entity-ն նախ բերում ենք, հետո map անում — Role.ToString()-ը projection-ի
        // ներսում SQL-ի չի թարգմանվում։
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user is null ? null : ToDto(user);
    }

    private async Task ResendIfCooledDownAsync(User user, string? language)
    {
        // Cooldown — առանց դրա մեկը կարող էր ուրիշի inbox-ը նամակներով լցնել
        if (user.EmailVerificationSentUtc is not null && DateTime.UtcNow - user.EmailVerificationSentUtc < ResendCooldown)
            return;

        var code = IssueVerificationCode(user);
        await _db.SaveChangesAsync();
        await SendVerificationEmailAsync(user, code, language);
    }

    /// <summary>Նոր կոդ է ստեղծում ու hash-ը դնում user-ի վրա (SaveChanges-ը կանչողինն է)։</summary>
    private static string IssueVerificationCode(User user)
    {
        // RandomNumberGenerator — Random-ը կանխատեսելի է, կոդը՝ ոչ
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        var now = DateTime.UtcNow;
        user.EmailVerificationCodeHash = Convert.ToHexString(HashVerificationCode(code));
        user.EmailVerificationExpiresUtc = now.AddMinutes(VerificationCodeValidMinutes);
        user.EmailVerificationSentUtc = now;
        user.EmailVerificationAttempts = 0;

        return code;
    }

    private static void ClearVerificationCode(User user)
    {
        user.EmailVerificationCodeHash = null;
        user.EmailVerificationExpiresUtc = null;
        user.EmailVerificationSentUtc = null;
        user.EmailVerificationAttempts = 0;
    }

    private async Task SendVerificationEmailAsync(User user, string code, string? language)
    {
        try
        {
            await _email.SendAsync(VerificationEmail.Build(user.Email, user.DisplayName, code, language, VerificationCodeValidMinutes));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Account-ը պահված է — user-ը կարող է "Resend" սեղմել կամ նորից գրանցվել
            _logger.LogError(ex, "Could not send verification email to {UserId}", user.Id);
            throw new EmailDeliveryException();
        }
    }

    private static byte[] HashVerificationCode(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static AuthResultDto ToDto(User user) => new()
    {
        UserId = user.Id,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Role = user.Role.ToString()
    };
}
