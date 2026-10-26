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
    public UserRole Role { get; set; } = UserRole.User;

    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockoutEndUtc { get; set; }

    // Email-ի հաստատման կոդը։ Պահում ենք միայն hash-ը (SHA-256) — բազայի արտահոսքը
    // չպիտի թույլ տա ուրիշի account-ը հաստատել։ Հաստատվելուց հետո դաշտերը մաքրվում են։
    public string? EmailVerificationCodeHash { get; set; }
    public DateTime? EmailVerificationExpiresUtc { get; set; }
    public DateTime? EmailVerificationSentUtc { get; set; }
    public int EmailVerificationAttempts { get; set; } = 0;

    public Guid? FamilyId { get; set; }
    public Family? Family { get; set; }

    public ICollection<FavoriteRecipe> Favorites { get; set; } = new List<FavoriteRecipe>();
    public ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();
}
