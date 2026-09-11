// Gialora.Application/Planning/GoalMatchers.cs
using Gialora.Shared.Goals;

namespace Gialora.Application.Planning;

/// <summary>
/// Կապում է Shared-ի նպատակների կատալոգը ռեցեպտի իրական դաշտերի հետ։
///
/// Ամեն նպատակ ՊԱՐՏԱԴԻՐ պիտի ունենա matcher. հակառակ դեպքում UI-ն կառաջարկեր
/// նպատակ, որը պլանավորիչը լուռ կանտեսեր — հենց այն վարքը, որ ուղղում ենք։
/// Static ctor-ը դա ստուգում է startup-ին, ոչ թե production-ում լռելյայն։
/// </summary>
public static class GoalMatchers
{
    private static readonly Dictionary<string, Func<PlanningCandidate, bool>> Matchers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["more-iron"] = c => c.IsIronRich,
            ["more-protein"] = c => c.IsHighProtein,
            ["picky-eater"] = c => c.IsKidFriendly,
            ["more-vegetables"] = c => c.VegetableCount >= 2,
            ["quicker-meals"] = c => c.TotalMinutes <= 30,
            ["lower-cost"] = c => c.CostPerServing is not null and <= 2.5m
        };

    static GoalMatchers()
    {
        var missing = NutritionGoals.All
            .Where(goal => !Matchers.ContainsKey(goal.Key))
            .Select(goal => goal.Key)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"These goals are offered to users but have no matcher: {string.Join(", ", missing)}. " +
                "Add one here, or remove the goal from NutritionGoals.All.");
        }
    }

    public static bool Matches(string goalKey, PlanningCandidate candidate) =>
        Matchers.TryGetValue(goalKey, out var matches) && matches(candidate);
}
