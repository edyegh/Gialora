// Gialora.Data/Entities/RecipeIngredient.cs
namespace Gialora.Data.Entities;

public class RecipeIngredient
{
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public Guid IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;

    public decimal Quantity { get; set; } // օր. 2.5 — կապված է Ingredient.Unit-ի հետ
}