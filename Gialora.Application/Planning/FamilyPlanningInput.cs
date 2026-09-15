// Gialora.Application/Planning/FamilyPlanningInput.cs
using Gialora.Data.Entities;
using Gialora.Shared.Enums;
using Gialora.Shared.Goals;

namespace Gialora.Application.Planning;

/// <summary>
/// Family + FamilyMember-ներ → engine-ի մուտք։ Առանձնացված է, որ "ո՞վ է հաշվի
/// առնվում" կանոնը մեկ տեղում լինի և թեստավորելի՝ առանց DB-ի։
///
/// Կանոնը՝ միայն ԱԿՏԻՎ անդամներն են հաշվվում։ Ոչ ակտիվ անդամի ալերգիաները,
/// սահմանափակումները, նպատակները, տարիքը և չափաբաժինը անտեսվում են, մինչև նա
/// նորից ակտիվանա։ Ջնջվածները (soft delete) նույնպես դուրս են։
/// </summary>
public static class FamilyPlanningInput
{
    /// <summary>Անդամները, որոնց համար իրականում պլանավորում ենք։</summary>
    public static List<FamilyMember> EatingMembers(Family family) =>
        family.FamilyMembers.Where(m => !m.IsDeleted && m.IsActive).ToList();

    /// <summary>
    /// Լռելյայն չափաբաժինը մեկ ճաշին։ Եթե անդամներ կան՝ ակտիվների թիվն է —
    /// այդպես անդամին ոչ ակտիվ դարձնելը ինքնաբերաբար փոքրացնում է գնումների ցանկը։
    /// Առանց անդամների՝ ընտանիքի ServingsPerMeal preference-ը։
    /// </summary>
    public static int DefaultServings(Family family)
    {
        var eating = EatingMembers(family).Count;
        return eating > 0 ? eating : family.ServingsPerMeal;
    }

    /// <summary>Ընտանիքի preference-ները + ակտիվ անդամների ալերգիաները → engine-ի սահմանափակումներ։</summary>
    public static PlanningConstraints BuildConstraints(Family family)
    {
        var members = EatingMembers(family);

        // Ալերգիաները ՄԻԱՎՈՐՎՈՒՄ են — մեկի ալերգիան ամբողջ ընտանիքի սահմանափակումն է
        var allergies = members
            .SelectMany(m => m.Allergies)
            .Concat(members.SelectMany(m => m.DietaryRestrictions))
            .Select(a => a.Trim().ToLowerInvariant())
            .Where(a => a.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var children = members.Where(m => m.MemberType == FamilyMemberType.Child).ToList();

        var youngest = children
            .Select(m => m.EffectiveAgeMonths)
            .Where(a => a.HasValue)
            .Select(a => a!.Value)
            .DefaultIfEmpty(int.MaxValue)
            .Min();

        // Անդամի "vegetarian" restriction-ը ամբողջ ընտանիքի դիետան չի դարձնում,
        // բայց ընտանիքի բացահայտ preference-ը՝ այո։
        var dietPreference = family.DietPreference;

        return new PlanningConstraints
        {
            MaxCookingTimeMinutes = family.MaxCookingTimeMinutes,
            DietPreference = dietPreference,
            PreferredCuisine = family.PreferredCuisine,
            Budget = family.Budget,
            PreferFreezerFriendly = family.PreferFreezerFriendly,
            ExcludedProteins = family.ExcludedProteins
                .Select(p => Enum.TryParse<ProteinType>(p, true, out var parsed) ? parsed : (ProteinType?)null)
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .ToHashSet(),
            DislikedIngredients = family.DislikedIngredients.ToHashSet(StringComparer.OrdinalIgnoreCase),
            Allergies = allergies,
            YoungestAgeMonths = youngest == int.MaxValue ? null : youngest,
            HasChildren = children.Count > 0,

            // Բոլոր ակտիվ անդամների նպատակները միավորվում են և վերածվում ճանաչված
            // բանալիների. "more iron"-ը այստեղից է հասնում scoring-ին։
            Goals = NutritionGoals
                .Resolve(members.SelectMany(m => m.Goals))
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
    }
}
