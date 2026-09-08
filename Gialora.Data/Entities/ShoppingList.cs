// Gialora.Data/Entities/ShoppingList.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

/// <summary>
/// Meal plan-ից ավտոմատ գեներացվող գնումների ցանկ (app structure §1, §3)։
/// Նույն բաղադրիչը մի քանի ռեցեպտից consolidate է արվում մեկ տողի մեջ։
/// </summary>
public class ShoppingList : BaseEntity
{
    public Guid FamilyId { get; set; }
    public Family Family { get; set; } = null!;

    /// <summary>Ո՞ր պլանից է գեներացվել (null = ձեռքով ստեղծված ցանկ)։</summary>
    public Guid? MealPlanId { get; set; }
    public MealPlan? MealPlan { get; set; }

    public string Name { get; set; } = string.Empty;
    public ShoppingListStatus Status { get; set; } = ShoppingListStatus.Active;

    public ICollection<ShoppingListItem> Items { get; set; } = new List<ShoppingListItem>();
}
