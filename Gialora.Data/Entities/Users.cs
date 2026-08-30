// Gialora.Data/Entities/User.cs
namespace Gialora.Data.Entities;

public enum UserRole
{
    User,
    Admin
}

public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public bool EmailConfirmed { get; set; } = false;
    public UserRole Role { get; set; } = UserRole.User; // ← ՆՈՐ

    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockoutEndUtc { get; set; }

    public Guid? FamilyId { get; set; }
    public Family? Family { get; set; }

    public ICollection<FamilyMember> FamilyMembers { get; set; } = new List<FamilyMember>();
}