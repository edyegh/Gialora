// Gialora.Data/Entities/Family.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

/// <summary>
/// Ընտանիքը + նրա preference-ները։ Meal-planning engine-ի ամբողջ մուտքային
/// տվյալը այստեղից և FamilyMember-ներից է գալիս (app structure §1, §4)։
/// </summary>
public class Family : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    // --- Preferences (app structure §1 "Selects preferences") ---
    public CuisineType PreferredCuisine { get; set; } = CuisineType.Mediterranean;

    /// <summary>Առավելագույն ընդհանուր ժամանակը (prep + cook) մեկ ճաշի համար։</summary>
    public int MaxCookingTimeMinutes { get; set; } = 45;

    /// <summary>Շաբաթվա մեջ քանի՞ օր են պատրաստելու (5 dinners/week)։</summary>
    public int CookingDaysPerWeek { get; set; } = 5;

    /// <summary>Քանի՞ չափաբաժին է պետք մեկ ճաշին — ռեցեպտները ավտոմատ scale են արվում։</summary>
    public int ServingsPerMeal { get; set; } = 4;

    public BudgetLevel Budget { get; set; } = BudgetLevel.Any;

    /// <summary>Ընտանիքի ընդհանուր դիետան (եթե դրված է, զտում ենք ըստ դրա)։</summary>
    public DietType? DietPreference { get; set; }

    /// <summary>"no fish" — բացառվող սպիտակուցները (CSV column)։</summary>
    public List<string> ExcludedProteins { get; set; } = new();

    /// <summary>Չսիրած բաղադրիչների անունները (normalized, lowercase)։</summary>
    public List<string> DislikedIngredients { get; set; } = new();

    public bool PreferFreezerFriendly { get; set; }

    public ICollection<User> Members { get; set; } = new List<User>();
    public ICollection<FamilyMember> FamilyMembers { get; set; } = new List<FamilyMember>();
    public ICollection<MealPlan> MealPlans { get; set; } = new List<MealPlan>();
}
