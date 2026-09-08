// Gialora.Data/Entities/ShoppingListItem.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

public class ShoppingListItem : BaseEntity
{
    public Guid ShoppingListId { get; set; }
    public ShoppingList ShoppingList { get; set; } = null!;

    /// <summary>null = user-ի ձեռքով ավելացրած ազատ տող ("batteries")։</summary>
    public Guid? IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Արդեն consolidate արված քանակը՝ բերված մեկ միավորի (օր. 1.7 kg)։</summary>
    public decimal Quantity { get; set; }
    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Gram;

    public IngredientCategory Category { get; set; } = IngredientCategory.Other;

    /// <summary>"User checks off ingredients while shopping" (app structure §1)։</summary>
    public bool IsChecked { get; set; }

    public bool IsOptional { get; set; }

    /// <summary>Որ ռեցեպտներից է եկել այս տողը — UI-ում ցույց ենք տալիս "for: X, Y"։</summary>
    public List<string> SourceRecipes { get; set; } = new();

    public int SortOrder { get; set; }
}
