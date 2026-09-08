// Gialora.Data/Entities/Recipe.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

/// <summary>
/// Ռեցեպտը պահվում է որպես ԴԱՏԱ, ոչ թե որպես հոդված (app structure §3)։
/// Հենց այս structured դաշտերն են թույլ տալիս ավտոմատացնել filtering-ը,
/// serving-ի վերահաշվարկը և meal-planning-ը։
/// </summary>
public class Recipe : BaseEntity
{
    public string Title { get; set; } = string.Empty;

    /// <summary>URL-friendly identifier — /recipes/chicken-meatballs (SEO-ի համար)։</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Քայլերը պահվում են մեկ text-ով, տողերով բաժանված (client-ը split է անում)։</summary>
    public string? Instructions { get; set; }

    public string? ImageUrl { get; set; }

    // --- Դասակարգում ---
    public MealType MealType { get; set; } = MealType.Dinner;
    public CuisineType Cuisine { get; set; } = CuisineType.Mediterranean;
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Easy;
    public DietType DietType { get; set; } = DietType.Meat;
    public ProteinType PrimaryProtein { get; set; } = ProteinType.None;

    // --- Ժամանակ և չափաբաժիններ ---
    public int PrepTimeMinutes { get; set; }
    public int CookTimeMinutes { get; set; }
    public int TotalTimeMinutes => PrepTimeMinutes + CookTimeMinutes;
    public int Servings { get; set; } = 4;

    // --- Ըստ տարիքի պիտանիությունը (app structure §A "Suitable ages") ---
    /// <summary>Նվազագույն տարիքը՝ ամիսներով (12 = 1 տարեկան)։ 0 = բոլորի համար։</summary>
    public int MinAgeMonths { get; set; } = 0;

    // --- Բնութագրիչ flag-եր (filtering-ի արագ ուղիներ) ---
    public bool IsFreezerFriendly { get; set; }
    public bool IsLunchboxFriendly { get; set; }
    public bool IsKidFriendly { get; set; }
    public bool IsHighProtein { get; set; }
    public bool IsIronRich { get; set; }
    /// <summary>"Cook once, save time later" — batch մեծ քանակով պատրաստելու համար։</summary>
    public bool IsBatchFriendly { get; set; }

    /// <summary>Մոտավոր արժեքը մեկ չափաբաժնի համար — budget filter-ի համար։</summary>
    public decimal? EstimatedCostPerServing { get; set; }

    // --- Հրապարակման workflow (admin panel, app structure §11) ---
    public bool IsPublished { get; set; } = false;
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>Ո՞ր admin-ն է ստեղծել/վերջին անգամ խմբագրել (audit trail)։</summary>
    public Guid CreatedByAdminId { get; set; }
    public User CreatedByAdmin { get; set; } = null!;

    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = new List<RecipeIngredient>();
    public ICollection<RecipeTag> RecipeTags { get; set; } = new List<RecipeTag>();
    public ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();
    public ICollection<FavoriteRecipe> Favorites { get; set; } = new List<FavoriteRecipe>();
}
