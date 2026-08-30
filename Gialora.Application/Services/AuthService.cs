// Gialora.Application/Services/AuthService.cs
using Microsoft.EntityFrameworkCore;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
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
            throw new InvalidOperationException("An account with this email already exists.");

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 12),
            DisplayName = dto.DisplayName.Trim(),
            Role = UserRole.User // ամեն ինքնուրույն գրանցում — միշտ սովորական user, ոչ admin
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New user registered: {UserId}", user.Id);

        return new AuthResultDto
        {
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString()
        };
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
            BCrypt.Net.BCrypt.Verify(dto.Password, "$2a$12$invalidsaltinvalidsaltinvalidsal0123456789abcdefghij");
            return null;
        }

        // Ստուգիր՝ account-ը lockout-ի մեջ չէ՞
        if (user.LockoutEndUtc is not null && user.LockoutEndUtc > DateTime.UtcNow)
        {
            _logger.LogWarning("Login attempt on locked account: {UserId}", user.Id);
            return null;
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

        return new AuthResultDto
        {
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString()
        };
    }
}