// Gialora.Data/Entities/MealPlanEntry.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

public class MealPlanEntry : BaseEntity
{
    public Guid MealPlanDayId { get; set; }
    public MealPlanDay MealPlanDay { get; set; } = null!;

    public MealType MealType { get; set; }

    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public int PlannedServings { get; set; } = 1;

    /// <summary>User-ը ձեռքով փոխե՞լ է այս ճաշը — regenerate անելիս այն չենք կորցնում։</summary>
    public bool IsLocked { get; set; }

    public int SortOrder { get; set; }
}
