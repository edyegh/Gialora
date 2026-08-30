// Gialora.Data/Entities/RecipeTag.cs
namespace Gialora.Data.Entities;

public class RecipeTag
{
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}