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
}

public class LoginDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

// Ինչ վերադարձնում ենք հաջող login/register-ից հետո (ՈՉ PasswordHash, ՈՉ ուրիշ sensitive դաշտ)
public class AuthResultDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}