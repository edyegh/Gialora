// Gialora.Shared/DTOs/RecipeDtos.cs
using System.ComponentModel.DataAnnotations;
using Gialora.Shared.Enums;

namespace Gialora.Shared.Dtos;

/// <summary>Ցուցակի "քարտը" — առանց ingredients/instructions-ի, որ payload-ը թեթև մնա։</summary>
public class RecipeSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }

    public MealType MealType { get; set; }
    public CuisineType Cuisine { get; set; }
    public DifficultyLevel Difficulty { get; set; }
    public DietType DietType { get; set; }
    public ProteinType PrimaryProtein { get; set; }

    public int PrepTimeMinutes { get; set; }
    public int CookTimeMinutes { get; set; }
    public int TotalTimeMinutes => PrepTimeMinutes + CookTimeMinutes;
    public int Servings { get; set; }
    public int MinAgeMonths { get; set; }

    public bool IsFreezerFriendly { get; set; }
    public bool IsLunchboxFriendly { get; set; }
    public bool IsKidFriendly { get; set; }
    public bool IsHighProtein { get; set; }
    public bool IsIronRich { get; set; }
    public bool IsBatchFriendly { get; set; }
    public bool IsPublished { get; set; }

    public double AverageRating { get; set; }
    public int RatingCount { get; set; }
    public bool IsFavorite { get; set; }

    public List<string> Tags { get; set; } = new();
}

/// <summary>Ամբողջական ռեցեպտը՝ մեկ էջի համար։</summary>
public class RecipeDetailDto : RecipeSummaryDto
{
    public string? Instructions { get; set; }
    public decimal? EstimatedCostPerServing { get; set; }
    public List<RecipeIngredientDto> Ingredients { get; set; } = new();

    /// <summary>Քայլերը՝ արդեն տողերի բաժանված (client-ը split չի անում)։</summary>
    public List<string> Steps { get; set; } = new();
}

public class RecipeIngredientDto
{
    public Guid IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public UnitOfMeasure Unit { get; set; }
    public IngredientCategory Category { get; set; }
    public string? Note { get; set; }
    public bool IsOptional { get; set; }
    public int SortOrder { get; set; }
}

// ---------------------------------------------------------------------------
// Create / Update
// ---------------------------------------------------------------------------

public class RecipeCreateDto
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required]
    public string Instructions { get; set; } = string.Empty;

    [Range(0, 600)]
    public int PrepTimeMinutes { get; set; }

    [Range(0, 600)]
    public int CookTimeMinutes { get; set; }

    [Range(1, 50)]
    public int Servings { get; set; } = 4;

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    public MealType MealType { get; set; } = MealType.Dinner;
    public CuisineType Cuisine { get; set; } = CuisineType.Mediterranean;
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Easy;
    public DietType DietType { get; set; } = DietType.Meat;
    public ProteinType PrimaryProtein { get; set; } = ProteinType.None;

    [Range(0, 1200)]
    public int MinAgeMonths { get; set; }

    public bool IsFreezerFriendly { get; set; }
    public bool IsLunchboxFriendly { get; set; }
    public bool IsKidFriendly { get; set; }
    public bool IsHighProtein { get; set; }
    public bool IsIronRich { get; set; }
    public bool IsBatchFriendly { get; set; }

    [Range(0, 1000)]
    public decimal? EstimatedCostPerServing { get; set; }

    public List<string> Tags { get; set; } = new();
    public List<RecipeIngredientInputDto> Ingredients { get; set; } = new();
}

/// <summary>Update-ը թույլ է տալիս նաև publish/unpublish անել նույն call-ով։</summary>
public class RecipeUpdateDto : RecipeCreateDto
{
    public bool IsPublished { get; set; }
}

public class RecipeIngredientInputDto
{
    [Required]
    public Guid IngredientId { get; set; }

    [Range(0.001, 100000)]
    public decimal Quantity { get; set; }

    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Gram;

    [StringLength(200)]
    public string? Note { get; set; }

    public bool IsOptional { get; set; }
    public int SortOrder { get; set; }
}

// ---------------------------------------------------------------------------
// Search / filter (app structure §9 "Recipe search/filter")
// ---------------------------------------------------------------------------

public class RecipeFilterDto
{
    public string? Search { get; set; }

    public MealType? MealType { get; set; }
    public CuisineType? Cuisine { get; set; }
    public DietType? DietType { get; set; }
    public DifficultyLevel? Difficulty { get; set; }

    /// <summary>Առավելագույն ընդհանուր ժամանակը (prep + cook)։</summary>
    public int? MaxTotalMinutes { get; set; }

    /// <summary>Երեխայի տարիքը ամիսներով — զտում ենք MinAgeMonths &lt;= այս արժեքը։</summary>
    public int? SuitableForAgeMonths { get; set; }

    public bool? FreezerFriendly { get; set; }
    public bool? LunchboxFriendly { get; set; }
    public bool? KidFriendly { get; set; }
    public bool? HighProtein { get; set; }
    public bool? IronRich { get; set; }
    public bool? BatchFriendly { get; set; }

    /// <summary>Tag slug-երը (AND logic — ռեցեպտը պիտի ունենա բոլորը)։</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Բացառվող բաղադրիչների անունները (ալերգիա/չսիրած)։</summary>
    public List<string> ExcludeIngredients { get; set; } = new();

    /// <summary>Միայն ընտրանի — պահանջում է authenticated user։</summary>
    public bool FavoritesOnly { get; set; }

    public RecipeSortOrder SortBy { get; set; } = RecipeSortOrder.Newest;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>Query string, որ client-ը կցում է GET /api/recipes-ին։</summary>
    public string ToQueryString()
    {
        var parts = new List<string>();

        void Add(string key, object? value)
        {
            if (value is null) return;
            var s = value.ToString();
            if (string.IsNullOrWhiteSpace(s)) return;
            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(s)}");
        }

        Add(nameof(Search), Search);
        Add(nameof(MealType), MealType);
        Add(nameof(Cuisine), Cuisine);
        Add(nameof(DietType), DietType);
        Add(nameof(Difficulty), Difficulty);
        Add(nameof(MaxTotalMinutes), MaxTotalMinutes);
        Add(nameof(SuitableForAgeMonths), SuitableForAgeMonths);
        Add(nameof(FreezerFriendly), FreezerFriendly);
        Add(nameof(LunchboxFriendly), LunchboxFriendly);
        Add(nameof(KidFriendly), KidFriendly);
        Add(nameof(HighProtein), HighProtein);
        Add(nameof(IronRich), IronRich);
        Add(nameof(BatchFriendly), BatchFriendly);
        if (FavoritesOnly) Add(nameof(FavoritesOnly), true);
        Add(nameof(SortBy), SortBy);
        Add(nameof(Page), Page);
        Add(nameof(PageSize), PageSize);

        foreach (var tag in Tags)
            Add(nameof(Tags), tag);

        foreach (var ing in ExcludeIngredients)
            Add(nameof(ExcludeIngredients), ing);

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}

public enum RecipeSortOrder
{
    Newest = 0,
    Title = 1,
    QuickestFirst = 2,
    HighestRated = 3
}
