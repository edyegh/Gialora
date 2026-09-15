// Gialora.Shared/Dtos/AdminUserDtos.cs
using System.ComponentModel.DataAnnotations;

namespace Gialora.Shared.Dtos;

// Admin-ի user-ների ցուցակի տողը (ՈՉ PasswordHash, ՈՉ lockout-ի մանրամասներ)
public class AdminUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public bool IsLockedOut { get; set; }
    public bool HasFamily { get; set; }

    /// <summary>Հենց այն admin-ն է, ով նայում է ցուցակին — client-ը իրեն ջնջելու կոճակը թաքցնում է։</summary>
    public bool IsCurrentUser { get; set; }
}

public class AdminUserCreateDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>"User" կամ "Admin"։</summary>
    [Required]
    public string Role { get; set; } = "User";
}

public class AdminSetPasswordDto
{
    [Required, StringLength(100, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;
}
