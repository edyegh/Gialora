// Gialora.Data/Entities/MealPlan.cs
namespace Gialora.Data.Entities;

public class MealPlan : BaseEntity
{
    public Guid FamilyId { get; set; }
    public Family Family { get; set; } = null!;

    public DateOnly WeekStartDate { get; set; } 

    public ICollection<MealPlanDay> Days { get; set; } = new List<MealPlanDay>();
}