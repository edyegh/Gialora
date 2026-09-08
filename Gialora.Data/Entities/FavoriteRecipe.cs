// Gialora.Data/Entities/FavoriteRecipe.cs
namespace Gialora.Data.Entities;

/// <summary>"Save/favorite recipes" — MVP-ի վերջին կետը (app structure §9)։</summary>
public class FavoriteRecipe
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
