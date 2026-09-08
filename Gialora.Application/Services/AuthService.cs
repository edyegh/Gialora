// Gialora.Application/Services/AuthService.cs
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
    private readonly ILogger<AuthService> _logger;

    // Login-ի attempt-երի սահմանաչափը (brute-force պաշտպանություն)
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // Իրական bcrypt hash (workFactor 12) գոյություն չունեցող password-ի համար։
    // Օգտագործվում է միայն որպես "dummy" ստուգում, որ անհայտ email-ի response time-ը
    // նույնը լինի, ինչ գոյություն ունեցողինը (timing attack-ի կանխարգելում)։
    private const string DummyPasswordHash =
        "$2a$12$tQdjcE1edb/OxXUyiAa7lOXy/tRAGBcHmsG9ua2NvGawythMt7eQq";

    public AuthService(GialoraDbContext db, ILogger<AuthService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AuthResultDto> RegisterAsync(RegisterDto dto)
    {
        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (existing is not null)
            throw new ConflictException("An account with this email already exists.");

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 12),
            DisplayName = dto.DisplayName.Trim(),
            Role = UserRole.User // ամեն ինքնուրույն գրանցում — միշտ սովորական user, ոչ admin
        };

        _db.Users.Add(user);

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

        _logger.LogInformation("New user registered: {UserId}", user.Id);

        return ToDto(user);
    }

    public async Task<AuthResultDto?> ValidateCredentialsAsync(LoginDto dto)
    {
        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
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

        return ToDto(user);
    }

    public async Task<AuthResultDto?> GetByIdAsync(Guid userId)
    {
        // Entity-ն նախ բերում ենք, հետո map անում — Role.ToString()-ը projection-ի
        // ներսում SQL-ի չի թարգմանվում։
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user is null ? null : ToDto(user);
    }

    private static AuthResultDto ToDto(User user) => new()
    {
        UserId = user.Id,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Role = user.Role.ToString()
    };
}
