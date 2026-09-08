// Gialora.Data/Entities/Feedback.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

/// <summary>
/// "👍 liked / 👎 didn't like / too difficult / kids didn't eat it" (app structure §1)։
/// Meal-planning engine-ը սա կարդում է որպես scoring signal։
/// </summary>
public class Feedback : BaseEntity
{
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>1–5. 0 = չի գնահատվել, միայն reaction/flag-եր են տրվել։</summary>
    public int Rating { get; set; }

    public FeedbackReaction Reaction { get; set; } = FeedbackReaction.None;

    public bool TooDifficult { get; set; }
    public bool KidsDidNotEat { get; set; }
    public bool TookTooLong { get; set; }

    public string? Comment { get; set; }
}
