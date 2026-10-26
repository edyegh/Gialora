// Gialora.Shared/Dtos/AuthDtos.cs
using System.ComponentModel.DataAnnotations;

namespace Gialora.Shared.Dtos;

public class RegisterDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>UI-ի լեզուն (en / hy / el) — հաստատման email-ը նույն լեզվով է գնում։</summary>
    [StringLength(5)]
    public string? Language { get; set; }
}

/// <summary>
/// Գրանցումը token ՉԻ վերադարձնում — account-ը ակտիվ է դառնում միայն email-ի
/// կոդը հաստատելուց հետո (POST /api/auth/verify-email)։
/// </summary>
public class RegisterResultDto
{
    public string Email { get; set; } = string.Empty;
}

public class VerifyEmailDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression(@"^\s*\d{6}\s*$", ErrorMessage = "The code must be 6 digits.")]
    public string Code { get; set; } = string.Empty;
}

public class ResendVerificationDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [StringLength(5)]
    public string? Language { get; set; }
}

public class LoginDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>UI-ի լեզուն — եթե email-ը չհաստատված է, նոր կոդը այս լեզվով է գնում։</summary>
    [StringLength(5)]
    public string? Language { get; set; }
}

// Ինչ վերադարձնում ենք հաջող login/register-ից հետո (ՈՉ PasswordHash, ՈՉ ուրիշ sensitive դաշտ)
public class AuthResultDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

// Login/Register-ի response-ը — մեկ տեղում, և՛ API-ի, և՛ Client-ի համար,
// որ property-ների անունները երբեք չշեղվեն իրարից
public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public AuthResultDto User { get; set; } = new();
}
