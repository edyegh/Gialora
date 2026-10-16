// Gialora.Tests/SlotMealTypeAndAllergyTests.cs
using Gialora.Application.Planning;
using Gialora.Shared.Enums;

namespace Gialora.Tests;

/// <summary>
/// Regression tests for two engine bugs found in review:
///  1. The engine ignored meal type, so with Breakfast + Dinner selected a dinner
///     stew could be placed in a breakfast slot.
///  2. Free-text allergies were guessed from a word list, so anything the list
///     missed ("almond powder", Armenian/Greek input) let unsafe recipes through.
///     Allergies are now structured (see the allergy section below).
/// </summary>
public class SlotMealTypeAndAllergyTests
{
    private readonly MealPlanningEngine _engine = new();

    private static PlanningCandidate Recipe(
        string title,
        MealType mealType = MealType.Dinner,
        ProteinType protein = ProteinType.Chicken,
        Allergen[]? allergens = null,
        bool unreviewed = false,
        params string[] ingredients) => new()
        {
            RecipeId = Guid.NewGuid(),
            Title = title,
            MealType = mealType,
            DietType = DietType.Meat,
            PrimaryProtein = protein,
            TotalMinutes = 30,
            Allergens = (allergens ?? Array.Empty<Allergen>()).ToHashSet(),
            HasUnreviewedIngredients = unreviewed,
            IngredientNames = ingredients.ToHashSet(StringComparer.OrdinalIgnoreCase),
            IngredientIds = ingredients.Select(_ => Guid.NewGuid()).ToHashSet()
        };

    private static PlanningConstraints Allergic(params Allergen[] allergens) => Allergic(null, allergens);

    private static PlanningConstraints Allergic(string? other, params Allergen[] allergens) => new()
    {
        MaxCookingTimeMinutes = 60,
        Allergens = allergens.ToHashSet(),
        OtherAllergies = other is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase) { other }
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
    //
    // Allergies are structured: the family ticks allergens from the EU-14 list and
    // admins mark each ingredient with the allergens it contains. The engine compares
    // the two — it never guesses from free text, so a compound like "Almond flour" is
    // caught because it is MARKED TreeNuts, not because its name contains "almond".

    [Fact]
    public void A_ticked_allergen_blocks_recipes_with_an_ingredient_marked_with_it()
    {
        var pool = new[]
        {
            Recipe("Almond biscuits", allergens: new[] { Allergen.TreeNuts, Allergen.Gluten }, ingredients: "Almond flour"),
            Recipe("Plain rice", ingredients: "Rice")
        };

        var allowed = _engine.Filter(pool, Allergic(Allergen.TreeNuts));

        Assert.Equal("Plain rice", Assert.Single(allowed).Title);
    }

    [Fact]
    public void Allergen_matching_uses_the_marks_not_the_name()
    {
        // Named innocently, but marked as containing sesame (e.g. a spice blend)
        var pool = new[] { Recipe("Herb crust chicken", allergens: new[] { Allergen.Sesame }, ingredients: "Za'atar") };

        Assert.Empty(_engine.Filter(pool, Allergic(Allergen.Sesame)));
    }

    [Fact]
    public void Peanuts_and_tree_nuts_are_separate_allergens()
    {
        var pool = new[] { Recipe("Walnut cake", allergens: new[] { Allergen.TreeNuts }, ingredients: "Walnuts") };

        Assert.Single(_engine.Filter(pool, Allergic(Allergen.Peanuts)));
        Assert.Empty(_engine.Filter(pool, Allergic(Allergen.TreeNuts)));
    }

    [Fact]
    public void Unreviewed_ingredients_are_unsafe_for_a_family_with_any_allergy()
    {
        // Created by CSV import, nobody has checked its allergens yet
        var pool = new[]
        {
            Recipe("Imported curry", unreviewed: true, ingredients: "Curry paste"),
            Recipe("Plain rice", ingredients: "Rice")
        };

        Assert.Equal("Plain rice", Assert.Single(_engine.Filter(pool, Allergic(Allergen.Celery))).Title);
        Assert.Equal("Plain rice", Assert.Single(_engine.Filter(pool, Allergic(other: "kiwi"))).Title);
    }

    [Fact]
    public void Unreviewed_ingredients_are_fine_for_a_family_without_allergies()
    {
        var pool = new[] { Recipe("Imported curry", unreviewed: true, ingredients: "Curry paste") };

        Assert.Single(_engine.Filter(pool, Allergic()));
    }

    [Fact]
    public void A_dietary_restriction_does_not_trigger_the_unreviewed_rule()
    {
        var constraints = new PlanningConstraints
        {
            MaxCookingTimeMinutes = 60,
            RestrictionTerms = new(StringComparer.OrdinalIgnoreCase) { "no pork" }
        };
        var pool = new[]
        {
            Recipe("Imported curry", unreviewed: true, ingredients: "Curry paste"),
            Recipe("Pork chops", ingredients: "Pork loin")
        };

        Assert.Equal("Imported curry", Assert.Single(_engine.Filter(pool, constraints)).Title);
    }

    [Theory]
    [InlineData("kiwi")]
    [InlineData("Kiwis")]
    [InlineData("kiwi allergy")]
    public void Other_allergies_not_in_the_list_match_ingredient_names(string other)
    {
        var pool = new[] { Recipe("Fruit salad", ingredients: "Kiwi"), Recipe("Plain rice", ingredients: "Rice") };

        Assert.Equal("Plain rice", Assert.Single(_engine.Filter(pool, Allergic(other: other))).Title);
    }
}
