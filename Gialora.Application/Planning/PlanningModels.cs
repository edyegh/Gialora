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

    /// <summary>Ռեցեպտի ալերգենները՝ բոլոր բաղադրիչների նշումների միավորումը։</summary>
    public HashSet<Allergen> Allergens { get; init; } = new();

    /// <summary>
    /// Գոնե մեկ բաղադրիչի ալերգենները admin-ը դեռ չի ստուգել։ Ալերգիա ունեցող
    /// ընտանիքի համար սա նշանակում է "անհայտ" = "ոչ անվտանգ"։
    /// </summary>
    public bool HasUnreviewedIngredients { get; init; }

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
    /// <summary>Ակտիվ անդամների ընտրած ալերգենները — ամենախիստ hard rule-ը։</summary>
    public HashSet<Allergen> Allergens { get; init; } = new();

    /// <summary>"Other" ալերգիաները (ցանկում չկան) — ստուգվում են միայն բաղադրիչի անվամբ։</summary>
    public HashSet<string> OtherAllergies { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Անդամների ազատ տեքստով սահմանափակումները ("no pork")՝ բաղադրիչի անվամբ։
    /// Ալերգիա չեն, ուստի չստուգված բաղադրիչների կանոնը սրանց վրա չի տարածվում։
    /// </summary>
    public HashSet<string> RestrictionTerms { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ընտանիքն ունի՞ որևէ ալերգիա (ցանկից կամ "Other")։</summary>
    public bool HasAnyAllergy => Allergens.Count > 0 || OtherAllergies.Count > 0;

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

    /// <summary>
    /// Յուրաքանչյուր slot-ի ճաշատեսակը (Count == SlotCount)։ Տրված լինելու դեպքում
    /// slot-ը լցվում է ՄԻԱՅՆ իր տեսակի ռեցեպտով — այլապես ընթրիքը կընկներ
    /// նախաճաշի տեղը։ null = ցանկացած թեկնածու ցանկացած slot-ի համար։
    /// </summary>
    public IReadOnlyList<MealType>? SlotMealTypes { get; init; }

    public MealPlanType PlanType { get; init; } = MealPlanType.Weekly;

    /// <summary>"Plan my week using as few different ingredients as possible" (app structure §5)։</summary>
    public bool OptimizeIngredientReuse { get; init; } = true;

    /// <summary>Ռեցեպտներ, որ արդեն կան պլանում (locked slot-երից) — չկրկնենք դրանք։</summary>
    public HashSet<Guid> AlreadyUsedRecipeIds { get; init; } = new();

    /// <summary>
    /// Պլանում մնացող ճաշերը (regenerate-ի ժամանակ)։ Դրանց սպիտակուցներն ու բաղադրիչները
    /// հաշվվում են scoring-ում — այլապես locked հավով երեքշաբթիից հետո չորեքշաբթին էլ
    /// հավ կստանար, կարծես պլանը դատարկ լիներ։
    /// </summary>
    public IReadOnlyCollection<PlanningCandidate> KeptMeals { get; init; } = Array.Empty<PlanningCandidate>();

    /// <summary>Regenerate-ը պիտի ուրիշ արդյունք տա — seed-ը դա է ապահովում։</summary>
    public int Seed { get; init; } = Environment.TickCount;
}

public class PlanningResult
{
    /// <summary>Ընտրված ռեցեպտները՝ պլանավորված հերթականությամբ։</summary>
    public List<PlanningCandidate> Selection { get; init; } = new();

    /// <summary>
    /// Selection[i]-ի slot-ի համարը։ SlotMealTypes-ի դեպքում որոշ slot-եր կարող են
    /// դատարկ մնալ (այդ տեսակի ռեցեպտները սպառվեցին), ուստի Selection-ի ինդեքսը
    /// այլևս slot-ի ինդեքսը չէ — service-ը պիտի սրանով տեղադրի ճաշերը։
    /// </summary>
    public List<int> SlotIndexes { get; init; } = new();

    /// <summary>Ինչ կանոններ կիրառվեցին / ինչ չհաջողվեց — UI-ում ցույց ենք տալիս։</summary>
    public List<string> Notes { get; init; } = new();

    public bool IsPartial { get; set; }
}
