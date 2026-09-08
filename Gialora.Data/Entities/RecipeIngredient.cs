// Gialora.Data/Entities/RecipeIngredient.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

/// <summary>
/// Ingredient | Quantity | Unit | Category աղյուսակի "Quantity + Unit" մասը (app structure §3)։
/// Unit-ը պահվում է ՀԵՆՑ ԱՅՍՏԵՂ, ոչ թե Ingredient-ի վրա. նույն "Onion"-ը մի ռեցեպտում
/// 100 գ է, մյուսում՝ 1 հատ։
/// </summary>
public class RecipeIngredient
{
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public Guid IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;

    public decimal Quantity { get; set; }
    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Gram;

    /// <summary>Ազատ նշում՝ "մանր կտրատած", "ըստ ճաշակի"։</summary>
    public string? Note { get; set; }

    /// <summary>Ոչ պարտադիր բաղադրիչը shopping list-ում առանձին է նշվում։</summary>
    public bool IsOptional { get; set; }

    /// <summary>Ցուցադրման հերթականությունը ռեցեպտի էջում։</summary>
    public int SortOrder { get; set; }
}
