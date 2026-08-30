// Gialora.Data/Entities/MealPlanEntry.cs
namespace Gialora.Data.Entities;

public enum MealType
{
    Breakfast,
    Lunch,
    Dinner,
    Snack
}

public class MealPlanEntry : BaseEntity
{
    public Guid MealPlanDayId { get; set; }
    public MealPlanDay MealPlanDay { get; set; } = null!;

    public MealType MealType { get; set; }

    public Recipe Recipe { get; set; } = null!;
    public Guid RecipeId { get; set; }

    public int PlannedServings { get; set; } = 1;
}