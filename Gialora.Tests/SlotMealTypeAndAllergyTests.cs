// Gialora.Tests/SlotMealTypeAndAllergyTests.cs
using Gialora.Application.Planning;
using Gialora.Shared.Enums;

namespace Gialora.Tests;

/// <summary>
/// Regression tests for two engine bugs found in review:
///  1. The engine ignored meal type, so with Breakfast + Dinner selected a dinner
///     stew could be placed in a breakfast slot.
///  2. Free-text allergies only matched a handful of exact tokens, so "tree nuts",
///     "almonds", "peanut allergy" or "shrimp" let unsafe recipes through.
/// </summary>
public class SlotMealTypeAndAllergyTests
{
    private readonly MealPlanningEngine _engine = new();

    private static PlanningCandidate Recipe(
        string title,
        MealType mealType = MealType.Dinner,
        ProteinType protein = ProteinType.Chicken,
        bool nuts = false,
        bool shellfish = false,
        params string[] ingredients) => new()
        {
            RecipeId = Guid.NewGuid(),
            Title = title,
            MealType = mealType,
            DietType = DietType.Meat,
            PrimaryProtein = protein,
            TotalMinutes = 30,
            ContainsNuts = nuts,
            ContainsShellfish = shellfish,
            IngredientNames = ingredients.ToHashSet(StringComparer.OrdinalIgnoreCase),
            IngredientIds = ingredients.Select(_ => Guid.NewGuid()).ToHashSet()
        };

    private static PlanningConstraints Allergic(params string[] allergies) => new()
    {
        MaxCookingTimeMinutes = 60,
        Allergies = allergies.ToHashSet(StringComparer.OrdinalIgnoreCase)
    };

    // ------------------------------------------------------------ meal types

    [Fact]
    public void Each_slot_only_gets_a_recipe_of_its_own_meal_type()
    {
        var pool = new List<PlanningCandidate>();
        for (var i = 0; i < 5; i++)
        {
            pool.Add(Recipe($"Breakfast {i}", MealType.Breakfast));
            pool.Add(Recipe($"Dinner {i}", MealType.Dinner));
        }

        var slotTypes = Enumerable.Range(0, 3)
            .SelectMany(_ => new[] { MealType.Breakfast, MealType.Dinner })
            .ToList();

        var result = _engine.Plan(pool, new PlanningRequest
        {
            Constraints = Allergic(),
            SlotCount = slotTypes.Count,
            SlotMealTypes = slotTypes,
            Seed = 1
        });

        Assert.Equal(6, result.Selection.Count);
        for (var i = 0; i < result.Selection.Count; i++)
            Assert.Equal(slotTypes[result.SlotIndexes[i]], result.Selection[i].MealType);
    }

    [Fact]
    public void Running_out_of_one_meal_type_still_fills_the_other_slots()
    {
        // One breakfast recipe, plenty of dinners, two days of Breakfast + Dinner
        var pool = new List<PlanningCandidate> { Recipe("Only breakfast", MealType.Breakfast) };
        for (var i = 0; i < 4; i++)
            pool.Add(Recipe($"Dinner {i}", MealType.Dinner));

        var slotTypes = new[] { MealType.Breakfast, MealType.Dinner, MealType.Breakfast, MealType.Dinner };

        var result = _engine.Plan(pool, new PlanningRequest
        {
            Constraints = Allergic(),
            SlotCount = slotTypes.Length,
            SlotMealTypes = slotTypes,
            Seed = 1
        });

        Assert.True(result.IsPartial);
        Assert.Equal(new[] { 0, 1, 3 }, result.SlotIndexes);
        Assert.All(result.Selection.Zip(result.SlotIndexes),
            x => Assert.Equal(slotTypes[x.Second], x.First.MealType));
    }

    [Fact]
    public void Without_slot_types_the_slot_indexes_are_sequential()
    {
        var pool = Enumerable.Range(0, 3).Select(i => Recipe($"R{i}")).ToList();

        var result = _engine.Plan(pool, new PlanningRequest { Constraints = Allergic(), SlotCount = 3, Seed = 1 });

        Assert.Equal(new[] { 0, 1, 2 }, result.SlotIndexes);
    }

    [Fact]
    public void Slot_types_must_match_the_slot_count()
    {
        Assert.Throws<ArgumentException>(() => _engine.Plan(
            new[] { Recipe("R") },
            new PlanningRequest
            {
                Constraints = Allergic(),
                SlotCount = 2,
                SlotMealTypes = new[] { MealType.Dinner }
            }));
    }

    // ------------------------------------------------------------ allergies

    [Theory]
    [InlineData("tree nuts")]
    [InlineData("Tree-Nuts")]
    [InlineData("peanut allergy")]
    [InlineData("almonds")]
    [InlineData("nut free")]
    public void Free_text_nut_allergies_block_recipes_flagged_as_containing_nuts(string allergy)
    {
        var pool = new[] { Recipe("Walnut cake", nuts: true, ingredients: "Walnuts"), Recipe("Plain rice", ingredients: "Rice") };

        var allowed = _engine.Filter(pool, Allergic(allergy));

        Assert.Equal("Plain rice", Assert.Single(allowed).Title);
    }

    [Fact]
    public void A_plural_allergy_matches_a_singular_ingredient_name_even_without_a_flag()
    {
        // The flag is missing (bad data) — the ingredient name must still catch it
        var pool = new[] { Recipe("Almond biscuits", ingredients: "Almond flour"), Recipe("Plain rice", ingredients: "Rice") };

        var allowed = _engine.Filter(pool, Allergic("almonds"));

        Assert.Equal("Plain rice", Assert.Single(allowed).Title);
    }

    [Theory]
    [InlineData("shrimp")]
    [InlineData("prawns")]
    [InlineData("mussels")]
    public void Specific_shellfish_names_use_the_shellfish_flag(string allergy)
    {
        var pool = new[] { Recipe("Seafood stew", shellfish: true, ingredients: "Mixed seafood"), Recipe("Plain rice", ingredients: "Rice") };

        var allowed = _engine.Filter(pool, Allergic(allergy));

        Assert.Equal("Plain rice", Assert.Single(allowed).Title);
    }

    [Fact]
    public void A_dietary_restriction_phrased_as_no_x_matches_the_ingredient()
    {
        var pool = new[] { Recipe("Pork chops", ingredients: "Pork loin"), Recipe("Plain rice", ingredients: "Rice") };

        var allowed = _engine.Filter(pool, Allergic("no pork"));

        Assert.Equal("Plain rice", Assert.Single(allowed).Title);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("  ")]
    public void A_placeholder_allergy_blocks_nothing(string allergy)
    {
        var pool = new[] { Recipe("Walnut cake", nuts: true, ingredients: "Walnuts"), Recipe("Plain rice", ingredients: "Rice") };

        var allowed = _engine.Filter(pool, Allergic(allergy));

        Assert.Equal(2, allowed.Count);
    }
}
