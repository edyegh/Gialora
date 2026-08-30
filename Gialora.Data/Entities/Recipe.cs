// Gialora.Data/Entities/Recipe.cs
namespace Gialora.Data.Entities;

public class Recipe : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Instructions { get; set; }

    public int PrepTimeMinutes { get; set; }
    public int CookTimeMinutes { get; set; }
    public int Servings { get; set; } = 4;

    public string? ImageUrl { get; set; }

    // Ո՞ր admin-ն է ստեղծել/վերջին անգամ խմբագրել (audit trail)
    public Guid CreatedByAdminId { get; set; }
    public User CreatedByAdmin { get; set; } = null!;

    public bool IsPublished { get; set; } = false; // draft/published workflow admin-ի համար

    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = new List<RecipeIngredient>();
    public ICollection<RecipeTag> RecipeTags { get; set; } = new List<RecipeTag>();
    public ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();
}