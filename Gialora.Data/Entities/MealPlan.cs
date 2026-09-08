// Gialora.Data/Entities/MealPlan.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

public class MealPlan : BaseEntity
{
    public Guid FamilyId { get; set; }
    public Family Family { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public MealPlanType PlanType { get; set; } = MealPlanType.Weekly;
    public MealPlanStatus Status { get; set; } = MealPlanStatus.Draft;

    public DateOnly WeekStartDate { get; set; }

    /// <summary>Քանի՞ չափաբաժնի համար է կառուցվել պլանը (scaling-ի հենակետը)։</summary>
    public int ServingsPerMeal { get; set; } = 4;

    public ICollection<MealPlanDay> Days { get; set; } = new List<MealPlanDay>();

    /// <summary>Ամեն պլան ունի առավելագույնը մեկ ակտիվ shopping list։</summary>
    public ShoppingList? ShoppingList { get; set; }
}
