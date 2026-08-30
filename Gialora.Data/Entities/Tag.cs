// Gialora.Data/Entities/Tag.cs
namespace Gialora.Data.Entities;

public class Tag : BaseEntity
{
    public string Name { get; set; } = string.Empty; // օր. "vegetarian", "quick", "high-protein"

    public ICollection<RecipeTag> RecipeTags { get; set; } = new List<RecipeTag>();
}