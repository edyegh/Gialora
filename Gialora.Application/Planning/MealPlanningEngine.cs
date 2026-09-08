// Gialora.Application/Planning/MealPlanningEngine.cs
using Gialora.Shared.Enums;

namespace Gialora.Application.Planning;

public interface IMealPlanningEngine
{
    /// <summary>Զտում է թեկնածուներին ընտանիքի կոշտ սահմանափակումներով։</summary>
    List<PlanningCandidate> Filter(IEnumerable<PlanningCandidate> candidates, PlanningConstraints constraints);

    PlanningResult Plan(IEnumerable<PlanningCandidate> candidates, PlanningRequest request);

    /// <summary>Մեկ ճաշի փոխարինում — "replace one meal" (app structure §1)։</summary>
    PlanningCandidate? PickReplacement(
        IEnumerable<PlanningCandidate> candidates,
        PlanningConstraints constraints,
        IReadOnlyCollection<PlanningCandidate> currentPlan,
        Guid replacingRecipeId,
        int seed);
}

/// <summary>
/// Rules-based recommendation engine (app structure §4)։
///
/// Դիտավորյալ ԱՌԱՆՑ AI-ի է. փաստաթուղթը հստակ ասում է՝ "Initially, don't build a
/// sophisticated AI system. Build a rules-based recommendation engine first."։
/// Ամբողջ logic-ը երկու քայլ է՝
///   1) HARD filter — ինչը ֆիզիկապես չի կարելի առաջարկել (ալերգիա, դիետա, տարիք, ժամանակ)։
///   2) SOFT scoring — ինչն է ավելի լավը (կրկնություն չլինի, բաղադրիչները վերաօգտագործվեն,
///      նախկինում հավանածները առաջ գան)։
/// </summary>
public class MealPlanningEngine : IMealPlanningEngine
{
    // Scoring-ի կշիռները մեկ տեղում են, որ դրանք tune անելը մեկ տողի փոփոխություն լինի
    private const double SameProteinAsPreviousDayPenalty = -6.0;
    private const double ProteinAlreadyUsedTwicePenalty = -2.5;
    private const double IngredientReuseBonusPerShared = 0.9;
    private const double MaxIngredientReuseBonus = 4.0;
    private const double HistoryWeight = 3.0;
    private const double FreezerPreferenceBonus = 1.5;
    private const double KidFriendlyBonus = 1.5;
    private const double VegetableQuotaBonus = 2.5;
    private const double LegumeQuotaBonus = 3.0;
    private const double CuisineMatchBonus = 1.0;
    private const double QuickMealBonus = 0.8;
    private const double RandomJitter = 1.2;

    // -----------------------------------------------------------------------
    // 1. HARD filters
    // -----------------------------------------------------------------------

    public List<PlanningCandidate> Filter(IEnumerable<PlanningCandidate> candidates, PlanningConstraints constraints)
        => candidates.Where(c => IsAllowed(c, constraints)).ToList();

    private static bool IsAllowed(PlanningCandidate candidate, PlanningConstraints constraints)
    {
        // Ժամանակ — "30-minute meals"
        if (constraints.MaxCookingTimeMinutes > 0 && candidate.TotalMinutes > constraints.MaxCookingTimeMinutes)
            return false;

        // Դիետա։ Vegetarian ընտանիքին vegan ռեցեպտը ընդունելի է, հակառակը՝ ոչ։
        if (constraints.DietPreference is { } diet && !DietAllows(diet, candidate.DietType))
            return false;

        // "no fish" և նման բացառումները
        if (constraints.ExcludedProteins.Contains(candidate.PrimaryProtein))
            return false;

        // Տարիք — ընտանիքի ամենափոքրի համար պիտի պիտանի լինի
        if (constraints.YoungestAgeMonths is { } youngest && candidate.MinAgeMonths > youngest)
            return false;

        // Ալերգիա — սա ամենակարևոր hard rule-ն է, երբեք soft score չի դառնում
        if (ViolatesAllergy(candidate, constraints.Allergies))
            return false;

        // Չսիրած բաղադրիչներ
        if (constraints.DislikedIngredients.Count > 0 &&
            candidate.IngredientNames.Any(name =>
                constraints.DislikedIngredients.Any(d => name.Contains(d, StringComparison.OrdinalIgnoreCase))))
            return false;

        // Բյուջե — Low = մինչև 3 միավոր/չափաբաժին, Medium = մինչև 6
        if (constraints.Budget != BudgetLevel.Any && candidate.CostPerServing is { } cost)
        {
            var ceiling = constraints.Budget switch
            {
                BudgetLevel.Low => 3m,
                BudgetLevel.Medium => 6m,
                _ => decimal.MaxValue
            };

            if (cost > ceiling)
                return false;
        }

        return true;
    }

    private static bool DietAllows(DietType familyDiet, DietType recipeDiet) => familyDiet switch
    {
        DietType.Vegan => recipeDiet == DietType.Vegan,
        DietType.Vegetarian => recipeDiet is DietType.Vegetarian or DietType.Vegan,
        DietType.Fish => recipeDiet != DietType.Meat,
        _ => true
    };

    private static bool ViolatesAllergy(PlanningCandidate candidate, IReadOnlyCollection<string> allergies)
    {
        foreach (var allergy in allergies)
        {
            var match = allergy.Trim().ToLowerInvariant();

            var blocked = match switch
            {
                "gluten" or "wheat" or "celiac" => candidate.ContainsGluten,
                "dairy" or "milk" or "lactose" => candidate.ContainsDairy,
                "nuts" or "nut" or "peanut" or "peanuts" or "tree-nuts" => candidate.ContainsNuts,
                "egg" or "eggs" => candidate.ContainsEgg,
                "fish" => candidate.ContainsFish,
                "shellfish" or "seafood" or "crustaceans" => candidate.ContainsShellfish,
                "soy" or "soya" => candidate.ContainsSoy,
                // Անհայտ ալերգենը համեմատում ենք բաղադրիչի անվան հետ — ավելի լավ է
                // մի քանի ռեցեպտ ավելորդ բացառել, քան ալերգիկ ճաշ առաջարկել
                _ => candidate.IngredientNames.Any(n => n.Contains(match, StringComparison.OrdinalIgnoreCase))
            };

            if (blocked)
                return true;
        }

        return false;
    }

    // -----------------------------------------------------------------------
    // 2. SOFT scoring + greedy selection
    // -----------------------------------------------------------------------

    public PlanningResult Plan(IEnumerable<PlanningCandidate> candidates, PlanningRequest request)
    {
        var pool = Filter(candidates, request.Constraints)
            .Where(c => !request.AlreadyUsedRecipeIds.Contains(c.RecipeId))
            .ToList();

        var result = new PlanningResult();

        if (pool.Count == 0)
        {
            result.IsPartial = true;
            result.Notes.Add("No recipes match your current preferences. Try relaxing the cooking time or dietary filters.");
            return result;
        }

        var random = new Random(request.Seed);
        var selected = new List<PlanningCandidate>();
        var usedIngredients = new HashSet<Guid>();
        var proteinUsage = new Dictionary<ProteinType, int>();

        // Batch & freeze պլանում միայն batch/freezer-ի պիտանի ուտեստներն են իմաստ ունենում
        if (request.PlanType == MealPlanType.BatchAndFreeze)
        {
            var batchPool = pool.Where(c => c.IsBatchFriendly || c.IsFreezerFriendly).ToList();
            if (batchPool.Count > 0)
            {
                pool = batchPool;
                result.Notes.Add("Only batch- and freezer-friendly recipes were considered.");
            }
            else
            {
                result.Notes.Add("No batch-friendly recipes available yet — showing regular recipes instead.");
            }
        }

        var targetVegetableMeals = (int)Math.Ceiling(request.SlotCount * 0.6);
        var targetLegumeMeals = Math.Max(1, request.SlotCount / 5);
        var vegetableMeals = 0;
        var legumeMeals = 0;

        for (var slot = 0; slot < request.SlotCount; slot++)
        {
            var previous = selected.LastOrDefault();

            var best = pool
                .Where(c => selected.All(s => s.RecipeId != c.RecipeId))
                .Select(c => new
                {
                    Candidate = c,
                    Score = Score(
                        c, request, previous, usedIngredients, proteinUsage,
                        vegetableMeals, targetVegetableMeals,
                        legumeMeals, targetLegumeMeals,
                        slot, random)
                })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (best is null)
            {
                // Ռեցեպտների բազան սպառվեց — ազնիվ ասում ենք, չենք կրկնում ուտեստները
                result.IsPartial = true;
                result.Notes.Add(
                    $"Only {selected.Count} of {request.SlotCount} meals could be planned — " +
                    "there aren't enough matching recipes yet.");
                break;
            }

            var chosen = best.Candidate;
            selected.Add(chosen);

            foreach (var ingredientId in chosen.IngredientIds)
                usedIngredients.Add(ingredientId);

            proteinUsage[chosen.PrimaryProtein] = proteinUsage.GetValueOrDefault(chosen.PrimaryProtein) + 1;

            if (chosen.VegetableCount >= 2) vegetableMeals++;
            if (IsLegume(chosen)) legumeMeals++;
        }

        result.Selection.AddRange(selected);
        AddSummaryNotes(result, selected, request, vegetableMeals, legumeMeals, targetLegumeMeals);
        return result;
    }

    private static double Score(
        PlanningCandidate candidate,
        PlanningRequest request,
        PlanningCandidate? previous,
        HashSet<Guid> usedIngredients,
        Dictionary<ProteinType, int> proteinUsage,
        int vegetableMeals,
        int targetVegetableMeals,
        int legumeMeals,
        int targetLegumeMeals,
        int slot,
        Random random)
    {
        var score = candidate.HistoryScore * HistoryWeight;

        // Կանոն. "Don't repeat the same protein two days in a row"
        if (previous is not null &&
            candidate.PrimaryProtein != ProteinType.None &&
            candidate.PrimaryProtein == previous.PrimaryProtein)
        {
            score += SameProteinAsPreviousDayPenalty;
        }

        // Բազմազանություն ամբողջ շաբաթվա կտրվածքով
        var timesUsed = proteinUsage.GetValueOrDefault(candidate.PrimaryProtein);
        if (candidate.PrimaryProtein != ProteinType.None && timesUsed >= 2)
            score += ProteinAlreadyUsedTwicePenalty * (timesUsed - 1);

        // Կանոն. "Use ingredients already selected elsewhere in the week" (app structure §5)
        if (request.OptimizeIngredientReuse && usedIngredients.Count > 0)
        {
            var shared = candidate.IngredientIds.Count(usedIngredients.Contains);
            score += Math.Min(shared * IngredientReuseBonusPerShared, MaxIngredientReuseBonus);
        }

        // Կանոն. "Include vegetables at least X times"
        if (vegetableMeals < targetVegetableMeals && candidate.VegetableCount >= 2)
            score += VegetableQuotaBonus;

        // Կանոն. "Include legumes X times/week"
        if (legumeMeals < targetLegumeMeals && IsLegume(candidate))
            score += LegumeQuotaBonus;

        if (request.Constraints.PreferFreezerFriendly && candidate.IsFreezerFriendly)
            score += FreezerPreferenceBonus;

        if (request.Constraints.HasChildren && candidate.IsKidFriendly)
            score += KidFriendlyBonus;

        if (request.Constraints.PreferredCuisine is { } cuisine && candidate.Cuisine == cuisine)
            score += CuisineMatchBonus;

        // Շաբաթվա սկզբում ավելի արագ ուտեստներ — աշխատանքային օրերն ամենալարվածն են
        if (slot < 2 && candidate.TotalMinutes <= 30)
            score += QuickMealBonus;

        // Առանց jitter-ի "regenerate the week"-ը միշտ նույն արդյունքը կտար
        score += random.NextDouble() * RandomJitter;

        return score;
    }

    private static bool IsLegume(PlanningCandidate candidate) =>
        candidate.PrimaryProtein == ProteinType.Legume ||
        candidate.Tags.Contains("legumes") ||
        candidate.Tags.Contains("beans") ||
        candidate.Tags.Contains("lentils");

    private static void AddSummaryNotes(
        PlanningResult result,
        List<PlanningCandidate> selected,
        PlanningRequest request,
        int vegetableMeals,
        int legumeMeals,
        int targetLegumeMeals)
    {
        if (selected.Count == 0)
            return;

        var distinctProteins = selected.Select(s => s.PrimaryProtein).Distinct().Count();
        result.Notes.Add($"{distinctProteins} different protein sources across {selected.Count} meals.");

        if (vegetableMeals > 0)
            result.Notes.Add($"{vegetableMeals} vegetable-rich meals.");

        if (legumeMeals >= targetLegumeMeals && targetLegumeMeals > 0)
            result.Notes.Add($"{legumeMeals} legume-based meal(s) included.");

        if (request.OptimizeIngredientReuse)
        {
            var totalIngredientSlots = selected.Sum(s => s.IngredientIds.Count);
            var distinctIngredients = selected.SelectMany(s => s.IngredientIds).Distinct().Count();

            if (totalIngredientSlots > distinctIngredients)
            {
                result.Notes.Add(
                    $"Shared ingredients across meals: {totalIngredientSlots - distinctIngredients} " +
                    $"repeats over {distinctIngredients} distinct items — fewer things to buy.");
            }
        }

        var freezerCount = selected.Count(s => s.IsFreezerFriendly);
        if (freezerCount > 0)
            result.Notes.Add($"{freezerCount} recipe(s) can be frozen for later.");
    }

    // -----------------------------------------------------------------------
    // 3. "Replace one meal"
    // -----------------------------------------------------------------------

    public PlanningCandidate? PickReplacement(
        IEnumerable<PlanningCandidate> candidates,
        PlanningConstraints constraints,
        IReadOnlyCollection<PlanningCandidate> currentPlan,
        Guid replacingRecipeId,
        int seed)
    {
        var keptPlan = currentPlan.Where(c => c.RecipeId != replacingRecipeId).ToList();

        var pool = Filter(candidates, constraints)
            .Where(c => keptPlan.All(k => k.RecipeId != c.RecipeId))
            .Where(c => c.RecipeId != replacingRecipeId) // նույն ուտեստը հետ չառաջարկենք
            .ToList();

        if (pool.Count == 0)
            return null;

        var random = new Random(seed);
        var usedIngredients = keptPlan.SelectMany(k => k.IngredientIds).ToHashSet();
        var proteinUsage = keptPlan
            .GroupBy(k => k.PrimaryProtein)
            .ToDictionary(g => g.Key, g => g.Count());

        var request = new PlanningRequest
        {
            Constraints = constraints,
            SlotCount = 1,
            OptimizeIngredientReuse = true,
            Seed = seed
        };

        return pool
            .OrderByDescending(c => Score(
                c, request, previous: null, usedIngredients, proteinUsage,
                vegetableMeals: keptPlan.Count(k => k.VegetableCount >= 2),
                targetVegetableMeals: (int)Math.Ceiling((keptPlan.Count + 1) * 0.6),
                legumeMeals: keptPlan.Count(IsLegume),
                targetLegumeMeals: Math.Max(1, (keptPlan.Count + 1) / 5),
                slot: 0,
                random))
            .First();
    }
}
