// Gialora.Data/Entities/MealPlanDay.cs
namespace Gialora.Data.Entities;

public class MealPlanDay : BaseEntity
{
    public Guid MealPlanId { get; set; }
    public MealPlan MealPlan { get; set; } = null!;

    public DateOnly Date { get; set; }

    public ICollection<MealPlanEntry> Entries { get; set; } = new List<MealPlanEntry>();
}