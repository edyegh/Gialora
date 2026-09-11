// Gialora.Application/Planning/PlanningModels.cs
using Gialora.Shared.Enums;

namespace Gialora.Application.Planning;

/// <summary>
/// Engine-ի համար "հարթեցված" ռեցեպտ։ Դիտավորյալ չենք փոխանցում EF entity-ներ —
/// engine-ը մաքուր, DB-ից անկախ և թեստավորելի է մնում։
/// </summary>
public class PlanningCandidate
{
    public Guid RecipeId { get; init; }
    public string Title { get; init; } = string.Empty;

    public MealType MealType { get; init; }
    public DietType DietType { get; init; }
    public ProteinType PrimaryProtein { get; init; }
    public CuisineType Cuisine { get; init; }
    public DifficultyLevel Difficulty { get; init; }

    public int TotalMinutes { get; init; }
    public int MinAgeMonths { get; init; }

    public bool IsFreezerFriendly { get; init; }
    public bool IsBatchFriendly { get; init; }
    public bool IsKidFriendly { get; init; }
    public bool IsIronRich { get; init; }
    public bool IsHighProtein { get; init; }

    public decimal? CostPerServing { get; init; }

    public HashSet<Guid> IngredientIds { get; init; } = new();
    public HashSet<string> IngredientNames { get; init; } = new();
    public HashSet<string> Tags { get; init; } = new();

    /// <summary>Քանի՞ բանջարեղեն կա — "include vegetables X times" կանոնի համար։</summary>
    public int VegetableCount { get; init; }

    /// <summary>Ալերգենների դրոշները՝ ամբողջ ռեցեպտի մակարդակով (ցանկացած բաղադրիչից)։</summary>
    public bool ContainsGluten { get; init; }
    public bool ContainsDairy { get; init; }
    public bool ContainsNuts { get; init; }
    public bool ContainsEgg { get; init; }
    public bool ContainsFish { get; init; }
    public bool ContainsShellfish { get; init; }
    public bool ContainsSoy { get; init; }

    /// <summary>Feedback/favorite-ից եկող նախնական միավորը (−1 … +2)։</summary>
    public double HistoryScore { get; set; }
}

/// <summary>Ընտանիքի սահմանափակումները՝ engine-ի ընթեռնելի տեսքով։</summary>
public class PlanningConstraints
{
    public int MaxCookingTimeMinutes { get; init; } = 45;
    public DietType? DietPreference { get; init; }
    public CuisineType? PreferredCuisine { get; init; }
    public BudgetLevel Budget { get; init; } = BudgetLevel.Any;
    public bool PreferFreezerFriendly { get; init; }

    public HashSet<ProteinType> ExcludedProteins { get; init; } = new();
    public HashSet<string> DislikedIngredients { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Allergies { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ընտանիքի ամենափոքր անդամի տարիքը ամիսներով (null = երեխա չկա)։</summary>
    public int? YoungestAgeMonths { get; init; }

    public bool HasChildren { get; init; }

    /// <summary>
    /// Ընտանիքի անդամների նպատակները՝ "more iron", "picky eater" (app structure §1)։
    /// Սրանք ՓԱՓՈՒԿ ազդանշաններ են, ոչ թե կոշտ զտիչներ. նպատակը ցանկություն է,
    /// ոչ թե սահմանափակում, ուստի այն բարձրացնում է միավորը, բայց ոչ մի ուտեստ
    /// ամբողջությամբ չի բացառում։
    /// </summary>
    public HashSet<string> Goals { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public class PlanningRequest
{
    public PlanningConstraints Constraints { get; init; } = new();

    /// <summary>Քանի՞ ճաշ ենք ընտրում (օր × ճաշատեսակ)։</summary>
    public int SlotCount { get; init; } = 5;

    public MealPlanType PlanType { get; init; } = MealPlanType.Weekly;

    /// <summary>"Plan my week using as few different ingredients as possible" (app structure §5)։</summary>
    public bool OptimizeIngredientReuse { get; init; } = true;

    /// <summary>Ռեցեպտներ, որ արդեն կան պլանում (locked slot-երից) — չկրկնենք դրանք։</summary>
    public HashSet<Guid> AlreadyUsedRecipeIds { get; init; } = new();

    /// <summary>Regenerate-ը պիտի ուրիշ արդյունք տա — seed-ը դա է ապահովում։</summary>
    public int Seed { get; init; } = Environment.TickCount;
}

public class PlanningResult
{
    /// <summary>Ընտրված ռեցեպտները՝ պլանավորված հերթականությամբ։</summary>
    public List<PlanningCandidate> Selection { get; init; } = new();

    /// <summary>Ինչ կանոններ կիրառվեցին / ինչ չհաջողվեց — UI-ում ցույց ենք տալիս։</summary>
    public List<string> Notes { get; init; } = new();

    public bool IsPartial { get; set; }
}
