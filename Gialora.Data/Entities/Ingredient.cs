// Gialora.Data/Entities/Ingredient.cs
namespace Gialora.Data.Entities;

public class Ingredient : BaseEntity
{
    public string Name { get; set; } = string.Empty; // օր. "Ձիթապտուղի ձեթ"
    public string Unit { get; set; } = string.Empty;  // հիմնական չափման միավոր (գ, հատ, բաժակ)

    // Ալերգեն flag-երը հեշտացնում են filtering-ը dietary restriction-ների հետ
    public bool ContainsGluten { get; set; } = false;
    public bool ContainsDairy { get; set; } = false;
    public bool ContainsNuts { get; set; } = false;

    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = new List<RecipeIngredient>();
}