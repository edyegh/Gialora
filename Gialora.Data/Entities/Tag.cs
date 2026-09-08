// Gialora.Data/Entities/Tag.cs
namespace Gialora.Data.Entities;

public class Tag : BaseEntity
{
    /// <summary>Normalized slug — միշտ lowercase, օր. "high-protein", "quick", "kid-friendly"։</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Ինչ ցույց տանք UI-ում, օր. "High protein"։</summary>
    public string DisplayName { get; set; } = string.Empty;

    public ICollection<RecipeTag> RecipeTags { get; set; } = new List<RecipeTag>();
}
