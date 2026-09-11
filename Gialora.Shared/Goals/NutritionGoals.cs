// Gialora.Shared/Goals/NutritionGoals.cs
namespace Gialora.Shared.Goals;

/// <summary>
/// Ընտանիքի նպատակների կատալոգը՝ "more iron", "picky eater" (app structure §1)։
///
/// Ապրում է Shared-ում, որովհետև ԵՐԿՈՒ կողմին էլ պետք է՝ engine-ին՝ ուտեստները
/// գնահատելու համար, UI-ին՝ ցույց տալու, թե որ նպատակներն են իրականում գործում։
/// Առանց մեկ ընդհանուր աղբյուրի էջը կխոստանար բաներ, որ պլանավորիչը չի անում։
///
/// Այստեղ միայն ՏԵՔՍՏՆ է։ Ռեցեպտի հետ համեմատությունը
/// Gialora.Application.Planning.GoalMatchers-ում է, քանի որ այն կախված է
/// PlanningCandidate-ից, որը Shared-ին հասանելի չէ։
/// </summary>
public static class NutritionGoals
{
    /// <param name="Key">Կանոնական բանալին — engine-ը սրանով է գտնում համապատասխանությունը։</param>
    /// <param name="Label">UI-ի տեքստը։</param>
    /// <param name="Keywords">Ազատ տեքստում որոնվող բառերը (lowercase)։</param>
    public record NutritionGoal(string Key, string Label, string[] Keywords);

    /// <summary>
    /// Ճանաչվող նպատակները։ Ամեն մեկը հենվում է ռեցեպտի ԱՌԿԱ դաշտի վրա, ուստի
    /// նոր նպատակ ավելացնելը պահանջում է, որ համապատասխան տվյալը գոյություն ունենա։
    /// </summary>
    public static readonly IReadOnlyList<NutritionGoal> All = new[]
    {
        new NutritionGoal("more-iron", "More iron", new[] { "iron" }),
        new NutritionGoal("more-protein", "More protein", new[] { "protein" }),
        new NutritionGoal("picky-eater", "Picky eater", new[] { "picky", "fussy" }),
        new NutritionGoal("more-vegetables", "More vegetables", new[] { "vegetable", "veggies", "veg" }),
        new NutritionGoal("quicker-meals", "Quicker meals", new[] { "quick", "fast", "less time" }),
        new NutritionGoal("lower-cost", "Lower cost", new[] { "budget", "cheap", "cost", "save money" })
    };

    /// <summary>Ազատ տեքստը վերածում է ճանաչված նպատակների։</summary>
    public static List<NutritionGoal> Resolve(IEnumerable<string> rawGoals)
    {
        var resolved = new List<NutritionGoal>();

        foreach (var raw in rawGoals)
        {
            var text = (raw ?? string.Empty).Trim();
            if (text.Length == 0)
                continue;

            var match = Match(text);

            if (match is not null && !resolved.Contains(match))
                resolved.Add(match);
        }

        return resolved;
    }

    /// <summary>
    /// Չճանաչված նպատակները։ UI-ն դրանք առանձին է նշում — լուռ անտեսելը
    /// հենց այն խնդիրն էր, որ ամբողջ ֆունկցիոնալությունը դարձնում էր անիմաստ։
    /// </summary>
    public static List<string> Unrecognised(IEnumerable<string> rawGoals) =>
        rawGoals
            .Select(g => (g ?? string.Empty).Trim())
            .Where(g => g.Length > 0 && Match(g) is null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static NutritionGoal? Match(string text) =>
        All.FirstOrDefault(goal =>
            goal.Key.Equals(text, StringComparison.OrdinalIgnoreCase) ||
            goal.Label.Equals(text, StringComparison.OrdinalIgnoreCase) ||
            goal.Keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
}
