// Gialora.Shared/DTOs/FamilyDtos.cs
using System.ComponentModel.DataAnnotations;
using Gialora.Shared.Enums;

namespace Gialora.Shared.Dtos;

public class FamilyMemberDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public FamilyMemberType MemberType { get; set; }
    public int? Age { get; set; }
    public int? AgeMonths { get; set; }
    public List<string> DietaryRestrictions { get; set; } = new();
    public List<string> Allergies { get; set; } = new();
    public List<string> Goals { get; set; } = new();
}

public class FamilyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<FamilyMemberDto> Members { get; set; } = new();
    public FamilyPreferencesDto Preferences { get; set; } = new();
}

/// <summary>
/// "Selects preferences" քայլը (app structure §1)։ Սա է meal-planning engine-ի
/// հիմնական մուտքը՝ ընտանիքի անդամների կողքին։
/// </summary>
public class FamilyPreferencesDto
{
    public CuisineType PreferredCuisine { get; set; } = CuisineType.Mediterranean;

    [Range(10, 300)]
    public int MaxCookingTimeMinutes { get; set; } = 45;

    [Range(1, 7)]
    public int CookingDaysPerWeek { get; set; } = 5;

    [Range(1, 20)]
    public int ServingsPerMeal { get; set; } = 4;

    public BudgetLevel Budget { get; set; } = BudgetLevel.Any;

    public DietType? DietPreference { get; set; }

    /// <summary>"no fish" — բացառվող սպիտակուցները։</summary>
    public List<ProteinType> ExcludedProteins { get; set; } = new();

    public List<string> DislikedIngredients { get; set; } = new();

    public bool PreferFreezerFriendly { get; set; }
}

public class FamilyUpdateDto
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public class FamilyMemberCreateDto
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public FamilyMemberType MemberType { get; set; } = FamilyMemberType.Adult;

    [Range(0, 120)]
    public int? Age { get; set; }

    [Range(0, 1440)]
    public int? AgeMonths { get; set; }

    public List<string> DietaryRestrictions { get; set; } = new();
    public List<string> Allergies { get; set; } = new();
    public List<string> Goals { get; set; } = new();
}

public class FamilyMemberUpdateDto : FamilyMemberCreateDto
{
}
