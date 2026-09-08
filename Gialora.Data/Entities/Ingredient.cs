// Gialora.Data/Entities/Ingredient.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

public class Ingredient : BaseEntity
{
    public string Name { get; set; } = string.Empty;       // օր. "Chicken mince"

    /// <summary>Լռելյայն չափման միավորը — նոր RecipeIngredient ավելացնելիս prefill է արվում։</summary>
    public UnitOfMeasure DefaultUnit { get; set; } = UnitOfMeasure.Gram;

    /// <summary>Խանութի "aisle"-ը — shopping list-ը խմբավորվում է ըստ սրա (app structure §3)։</summary>
    public IngredientCategory Category { get; set; } = IngredientCategory.Other;

    // Ալերգեն flag-երը հեշտացնում են filtering-ը dietary restriction-ների հետ
    public bool ContainsGluten { get; set; }
    public bool ContainsDairy { get; set; }
    public bool ContainsNuts { get; set; }
    public bool ContainsEgg { get; set; }
    public bool ContainsFish { get; set; }
    public bool ContainsShellfish { get; set; }
    public bool ContainsSoy { get; set; }

    /// <summary>"Pantry staple" — աղ, ձեթ, պղպեղ. shopping list-ում կարելի է առանձնացնել։</summary>
    public bool IsPantryStaple { get; set; }

    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = new List<RecipeIngredient>();
}
