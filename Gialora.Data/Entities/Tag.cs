// Gialora.Data/Entities/Tag.cs
namespace Gialora.Data.Entities;

public class Tag : BaseEntity
{
    /// <summary>Normalized slug — միշտ lowercase, օր. "high-protein", "quick", "kid-friendly"։</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>UI-ում ցուցադրվող անունը՝ ամեն լեզվով մեկ տող։</summary>
    public ICollection<TagTranslation> Translations { get; set; } = new List<TagTranslation>();

    public ICollection<RecipeTag> RecipeTags { get; set; } = new List<RecipeTag>();
}
