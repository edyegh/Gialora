// Gialora.Data/Entities/Feedback.cs
namespace Gialora.Data.Entities;

public class Feedback : BaseEntity
{
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public int Rating { get; set; } // 1-5
    public string? Comment { get; set; }
}