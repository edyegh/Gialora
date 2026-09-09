// Gialora.Tests/MealPlanningEngineTests.cs
using Gialora.Application.Planning;
using Gialora.Shared.Enums;

namespace Gialora.Tests;

/// <summary>
/// The planning rules from the brief. The engine is deliberately free of any
/// database dependency so these can be plain in-memory tests.
/// </summary>
public class MealPlanningEngineTests
{
    private readonly MealPlanningEngine _engine = new();

    private static PlanningCandidate Recipe(
        string title,
        ProteinType protein = ProteinType.Chicken,
        int minutes = 30,
        int minAgeMonths = 0,
        DietType diet = DietType.Meat,
        bool nuts = false,
        bool fish = false,
        bool freezer = false,
        bool batch = false,
        int vegetables = 0,
        decimal? cost = null,
        params string[] ingredients) => new()
        {
            RecipeId = Guid.NewGuid(),
            Title = title,
            MealType = MealType.Dinner,
            DietType = diet,
            PrimaryProtein = protein,
            Cuisine = CuisineType.Mediterranean,
            Difficulty = DifficultyLevel.Easy,
            TotalMinutes = minutes,
            MinAgeMonths = minAgeMonths,
            IsFreezerFriendly = freezer,
            IsBatchFriendly = batch,
            VegetableCount = vegetables,
            ContainsNuts = nuts,
            ContainsFish = fish,
            CostPerServing = cost,
            IngredientNames = ingredients.ToHashSet(StringComparer.OrdinalIgnoreCase),
            IngredientIds = ingredients.Select(_ => Guid.NewGuid()).ToHashSet()
        };

    private static PlanningConstraints Constraints(
        int maxMinutes = 60,
        DietType? diet = null,
        int? youngestAgeMonths = null,
        params string[] allergies) => new()
        {
            MaxCookingTimeMinutes = maxMinutes,
            DietPreference = diet,
            YoungestAgeMonths = youngestAgeMonths,
            Allergies = allergies.ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

    // ------------------------------------------------------------ hard filters

    [Fact]
    public void Recipes_over_the_time_limit_are_excluded()
    {
        var pool = new[] { Recipe("Quick", minutes: 25), Recipe("Slow", minutes: 90) };

        var allowed = _engine.Filter(pool, Constraints(maxMinutes: 30));

        Assert.Single(allowed);
        Assert.Equal("Quick", allowed[0].Title);
    }

    [Fact]
    public void An_allergy_excludes_the_recipe_outright()
    {
        var pool = new[]
        {
            Recipe("Tahini dip", nuts: true),
            Recipe("Tomato pasta")
        };

        var allowed = _engine.Filter(pool, Constraints(allergies: "nuts"));

        Assert.Single(allowed);
        Assert.Equal("Tomato pasta", allowed[0].Title);
    }

    [Fact]
    public void An_unrecognised_allergy_falls_back_to_matching_ingredient_names()
    {
        // Being over-cautious is the correct failure mode for an allergy.
        var pool = new[]
        {
            Recipe("Celery soup", ingredients: new[] { "Celery", "Onion" }),
            Recipe("Tomato pasta", ingredients: new[] { "Tomato", "Pasta" })
        };

        var allowed = _engine.Filter(pool, Constraints(allergies: "celery"));

        Assert.Single(allowed);
        Assert.Equal("Tomato pasta", allowed[0].Title);
    }

    [Fact]
    public void Excluded_proteins_remove_matching_recipes()
    {
        var pool = new[]
        {
            Recipe("Baked salmon", protein: ProteinType.Fish),
            Recipe("Chicken traybake", protein: ProteinType.Chicken)
        };

        var constraints = new PlanningConstraints
        {
            MaxCookingTimeMinutes = 60,
            ExcludedProteins = new HashSet<ProteinType> { ProteinType.Fish }
        };

        var allowed = _engine.Filter(pool, constraints);

        Assert.Single(allowed);
        Assert.Equal("Chicken traybake", allowed[0].Title);
    }

    [Fact]
    public void Recipes_unsuitable_for_the_youngest_child_are_excluded()
    {
        var pool = new[]
        {
            Recipe("Mild bake", minAgeMonths: 12),
            Recipe("Spicy stew", minAgeMonths: 36)
        };

        var allowed = _engine.Filter(pool, Constraints(youngestAgeMonths: 24));

        Assert.Single(allowed);
        Assert.Equal("Mild bake", allowed[0].Title);
    }

    [Fact]
    public void A_vegetarian_household_accepts_vegan_but_not_the_reverse()
    {
        var pool = new[]
        {
            Recipe("Lentil soup", diet: DietType.Vegan),
            Recipe("Cheese bake", diet: DietType.Vegetarian),
            Recipe("Beef ragu", diet: DietType.Meat)
        };

        var vegetarian = _engine.Filter(pool, Constraints(diet: DietType.Vegetarian));
        Assert.Equal(2, vegetarian.Count);

        var vegan = _engine.Filter(pool, Constraints(diet: DietType.Vegan));
        Assert.Single(vegan);
        Assert.Equal("Lentil soup", vegan[0].Title);
    }

    [Fact]
    public void Disliked_ingredients_exclude_the_whole_recipe()
    {
        var pool = new[]
        {
            Recipe("Aubergine bake", ingredients: new[] { "Aubergine", "Tomato" }),
            Recipe("Tomato pasta", ingredients: new[] { "Tomato", "Pasta" })
        };

        var constraints = new PlanningConstraints
        {
            MaxCookingTimeMinutes = 60,
            DislikedIngredients = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aubergine" }
        };

        var allowed = _engine.Filter(pool, constraints);

        Assert.Single(allowed);
        Assert.Equal("Tomato pasta", allowed[0].Title);
    }

    [Fact]
    public void A_low_budget_excludes_expensive_recipes_but_keeps_uncosted_ones()
    {
        var pool = new[]
        {
            Recipe("Cheap lentils", cost: 1.0m),
            Recipe("Steak", cost: 9.0m),
            Recipe("Unpriced")           // no cost recorded
        };

        var constraints = new PlanningConstraints
        {
            MaxCookingTimeMinutes = 60,
            Budget = BudgetLevel.Low
        };

        var allowed = _engine.Filter(pool, constraints).Select(c => c.Title).ToList();

        Assert.Contains("Cheap lentils", allowed);
        Assert.Contains("Unpriced", allowed);
        Assert.DoesNotContain("Steak", allowed);
    }

    // --------------------------------------------------------- soft scoring

    [Fact]
    public void The_same_protein_is_not_planned_two_days_running()
    {
        // Four chicken dishes and four alternatives: the alternation must hold.
        var pool = new List<PlanningCandidate>();
        for (var i = 0; i < 4; i++)
        {
            pool.Add(Recipe($"Chicken {i}", protein: ProteinType.Chicken));
            pool.Add(Recipe($"Legume {i}", protein: ProteinType.Legume));
        }

        var result = _engine.Plan(pool, new PlanningRequest
        {
            Constraints = Constraints(),
            SlotCount = 6,
            Seed = 12345
        });

        Assert.Equal(6, result.Selection.Count);

        for (var i = 1; i < result.Selection.Count; i++)
        {
            Assert.NotEqual(
                result.Selection[i - 1].PrimaryProtein,
                result.Selection[i].PrimaryProtein);
        }
    }

    [Fact]
    public void A_recipe_is_never_planned_twice_in_the_same_week()
    {
        var pool = Enumerable.Range(0, 8).Select(i => Recipe($"Meal {i}")).ToList();

        var result = _engine.Plan(pool, new PlanningRequest
        {
            Constraints = Constraints(),
            SlotCount = 5,
            Seed = 99
        });

        Assert.Equal(5, result.Selection.Distinct().Count());
    }

    [Fact]
    public void Running_out_of_recipes_reports_a_partial_plan_instead_of_repeating()
    {
        var pool = new[] { Recipe("Only one") };

        var result = _engine.Plan(pool, new PlanningRequest
        {
            Constraints = Constraints(),
            SlotCount = 5,
            Seed = 7
        });

        Assert.Single(result.Selection);
        Assert.True(result.IsPartial);
        Assert.Contains(result.Notes, n => n.Contains("Only 1 of 5"));
    }

    [Fact]
    public void Locked_meals_are_not_offered_again()
    {
        var keep = Recipe("Friday pasta", protein: ProteinType.None);
        var pool = new List<PlanningCandidate> { keep };
        pool.AddRange(Enumerable.Range(0, 5).Select(i => Recipe($"Other {i}")));

        var result = _engine.Plan(pool, new PlanningRequest
        {
            Constraints = Constraints(),
            SlotCount = 3,
            AlreadyUsedRecipeIds = new HashSet<Guid> { keep.RecipeId },
            Seed = 3
        });

        Assert.DoesNotContain(result.Selection, c => c.RecipeId == keep.RecipeId);
    }

    [Fact]
    public void Recipes_the_family_disliked_sink_below_neutral_ones()
    {
        var disliked = Recipe("Lentil soup", protein: ProteinType.Legume);
        disliked.HistoryScore = -4.5; // 👎 plus "kids didn't eat it"

        var neutral = Recipe("Chickpea stew", protein: ProteinType.Legume);

        var result = _engine.Plan(new[] { disliked, neutral }, new PlanningRequest
        {
            Constraints = Constraints(),
            SlotCount = 1,
            Seed = 42
        });

        Assert.Equal("Chickpea stew", result.Selection[0].Title);
    }

    [Fact]
    public void Batch_plans_only_consider_batch_or_freezer_friendly_recipes()
    {
        var pool = new[]
        {
            Recipe("Fresh salad"),
            Recipe("Beef ragu", protein: ProteinType.Beef, freezer: true),
            Recipe("Lentil soup", protein: ProteinType.Legume, batch: true)
        };

        var result = _engine.Plan(pool, new PlanningRequest
        {
            Constraints = Constraints(),
            SlotCount = 2,
            PlanType = MealPlanType.BatchAndFreeze,
            Seed = 5
        });

        Assert.All(result.Selection, c => Assert.True(c.IsFreezerFriendly || c.IsBatchFriendly));
        Assert.DoesNotContain(result.Selection, c => c.Title == "Fresh salad");
    }

    [Fact]
    public void Regenerating_with_a_different_seed_can_produce_a_different_plan()
    {
        // Without the jitter, "regenerate the week" would look like a dead button.
        var pool = Enumerable.Range(0, 12)
            .Select(i => Recipe($"Meal {i}", protein: (ProteinType)(i % 5 + 1)))
            .ToList();

        var first = _engine.Plan(pool, new PlanningRequest
            { Constraints = Constraints(), SlotCount = 5, Seed = 1 });

        var second = _engine.Plan(pool, new PlanningRequest
            { Constraints = Constraints(), SlotCount = 5, Seed = 2 });

        Assert.NotEqual(
            first.Selection.Select(c => c.Title),
            second.Selection.Select(c => c.Title));
    }

    [Fact]
    public void The_same_seed_produces_the_same_plan()
    {
        var pool = Enumerable.Range(0, 10).Select(i => Recipe($"Meal {i}")).ToList();

        var first = _engine.Plan(pool, new PlanningRequest
            { Constraints = Constraints(), SlotCount = 4, Seed = 777 });

        var second = _engine.Plan(pool, new PlanningRequest
            { Constraints = Constraints(), SlotCount = 4, Seed = 777 });

        Assert.Equal(
            first.Selection.Select(c => c.Title),
            second.Selection.Select(c => c.Title));
    }

    [Fact]
    public void An_empty_pool_reports_why_rather_than_throwing()
    {
        var result = _engine.Plan(Array.Empty<PlanningCandidate>(), new PlanningRequest
        {
            Constraints = Constraints(),
            SlotCount = 5
        });

        Assert.Empty(result.Selection);
        Assert.True(result.IsPartial);
        Assert.NotEmpty(result.Notes);
    }

    // ------------------------------------------------------------ replacement

    [Fact]
    public void Swapping_a_meal_never_returns_the_same_recipe_or_one_already_planned()
    {
        var current = Recipe("Currently planned");
        var alsoPlanned = Recipe("Also planned");
        var alternative = Recipe("Alternative");

        var replacement = _engine.PickReplacement(
            new[] { current, alsoPlanned, alternative },
            Constraints(),
            new[] { current, alsoPlanned },
            current.RecipeId,
            seed: 1);

        Assert.NotNull(replacement);
        Assert.Equal("Alternative", replacement!.Title);
    }

    [Fact]
    public void Swapping_returns_null_when_nothing_else_qualifies()
    {
        // The honest answer when the catalogue is small, not an exception.
        var current = Recipe("The only option");

        var replacement = _engine.PickReplacement(
            new[] { current },
            Constraints(),
            new[] { current },
            current.RecipeId,
            seed: 1);

        Assert.Null(replacement);
    }
}
