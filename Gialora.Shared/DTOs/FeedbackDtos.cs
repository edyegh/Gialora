// Gialora.Shared/DTOs/FeedbackDtos.cs
using System.ComponentModel.DataAnnotations;
using Gialora.Shared.Enums;

namespace Gialora.Shared.Dtos;

/// <summary>
/// "👍 liked / 👎 didn't like / too difficult / kids didn't eat it" (app structure §1)։
/// Engine-ը սա կարդում է որպես scoring signal հաջորդ պլանի համար։
/// </summary>
public class FeedbackDto
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public string RecipeTitle { get; set; } = string.Empty;
    public int Rating { get; set; }
    public FeedbackReaction Reaction { get; set; }
    public bool TooDifficult { get; set; }
    public bool KidsDidNotEat { get; set; }
    public bool TookTooLong { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class FeedbackUpsertDto
{
    [Range(0, 5)]
    public int Rating { get; set; }

    public FeedbackReaction Reaction { get; set; } = FeedbackReaction.None;

    public bool TooDifficult { get; set; }
    public bool KidsDidNotEat { get; set; }
    public bool TookTooLong { get; set; }

    [StringLength(1000)]
    public string? Comment { get; set; }
}

public class RecipeRatingSummaryDto
{
    public Guid RecipeId { get; set; }
    public double AverageRating { get; set; }
    public int RatingCount { get; set; }
    public int LikedCount { get; set; }
    public int DislikedCount { get; set; }
    public FeedbackDto? MyFeedback { get; set; }
}
