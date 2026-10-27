using Gialora.Application.Common;
using Gialora.Application.Services;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gialora.Tests;

/// <summary>
/// Data written in one UI language must read correctly in another: disliked ingredients are
/// catalogue ids, planner notes are translation keys, and shopping-list names are looked up
/// at read time instead of being frozen in the language the list was generated in.
/// </summary>
public class LanguageIndependenceTests
{
    private readonly GialoraDbContext _db = new(new DbContextOptionsBuilder<GialoraDbContext>()
        .UseInMemoryDatabase($"language-{Guid.NewGuid()}")
        .Options);

    private FamilyService Families(string language) =>
        new(_db, new FixedContentLanguage(language), NullLogger<FamilyService>.Instance);

    private ShoppingListService Lists(string language) =>
        new(_db, Families(language), new FixedContentLanguage(language), NullLogger<ShoppingListService>.Instance);

    private Ingredient AddIngredient(IngredientCategory category, params (string Language, string Name)[] names)
    {
        var ingredient = new Ingredient { Category = category, AllergensReviewed = true };
        _db.Ingredients.Add(ingredient);
        foreach (var (language, name) in names)
            _db.IngredientTranslations.Add(new IngredientTranslation { IngredientId = ingredient.Id, Language = language, Name = name });
        return ingredient;
    }

    private (User User, Family Family) AddUserWithFamily()
    {
        var family = new Family { Name = "Test family" };
        var user = new User { Email = "cook@test.local", DisplayName = "Cook", FamilyId = family.Id };
        _db.Families.Add(family);
        _db.Users.Add(user);
        _db.SaveChanges();
        return (user, family);
    }

    // ------------------------------------------------------------ planner notes

    [Fact]
    public void Planning_notes_round_trip_through_the_codec()
    {
        var encoded = PlanningNotes.Encode(PlanningNotes.GoalMatched, "Goal.more-iron", 2, 3);

        Assert.True(PlanningNotes.TryDecode(encoded, out var key, out var args));
        Assert.Equal(PlanningNotes.GoalMatched, key);
        Assert.Equal(new[] { "Goal.more-iron", "2", "3" }, args);
    }

    [Fact]
    public void Old_english_notes_are_not_mistaken_for_keys()
    {
        // Plans generated before notes became keys still hold English sentences
        Assert.False(PlanningNotes.TryDecode("5 different protein sources across 5 meals.", out _, out _));
        Assert.False(PlanningNotes.TryDecode(null, out _, out _));
    }

    // ------------------------------------------------------------ disliked ingredients

    [Fact]
    public async Task Disliked_ingredient_picks_are_stored_by_id_and_named_in_the_reader_language()
    {
        var garlic = AddIngredient(IngredientCategory.Vegetables, ("en", "Garlic"), ("hy", "Սխտոր"));
        var (user, _) = AddUserWithFamily();

        // Saved from the Armenian UI, including an id that does not exist
        await Families("hy").UpdatePreferencesAsync(user.Id, new FamilyPreferencesDto
        {
            DislikedIngredientIds = new() { garlic.Id, Guid.NewGuid() },
            DislikedIngredients = new() { "  Kiwi " }
        });

        var english = (await Families("en").GetOrCreateFamilyAsync(user.Id)).Preferences;

        Assert.Equal(new[] { garlic.Id }, english.DislikedIngredientIds);
        Assert.Equal(new[] { "Garlic" }, english.DislikedIngredientNames);
        Assert.Equal(new[] { "kiwi" }, english.DislikedIngredients);
    }

    // ------------------------------------------------------------ shopping lists

    [Fact]
    public async Task A_list_generated_in_english_reads_in_armenian()
    {
        var garlic = AddIngredient(IngredientCategory.Vegetables, ("en", "Garlic"), ("hy", "Սխտոր"));
        var rice = AddIngredient(IngredientCategory.Grains, ("en", "Rice"));   // no Armenian name yet

        var recipe = new Recipe { Slug = "garlic-rice", Servings = 4, IsPublished = true };
        _db.Recipes.Add(recipe);
        _db.RecipeTranslations.AddRange(
            new RecipeTranslation { RecipeId = recipe.Id, Language = "en", Title = "Garlic rice" },
            new RecipeTranslation { RecipeId = recipe.Id, Language = "hy", Title = "Սխտորով բրինձ" });
        _db.RecipeIngredients.AddRange(
            new RecipeIngredient { RecipeId = recipe.Id, IngredientId = garlic.Id, Quantity = 2, Unit = UnitOfMeasure.Piece },
            new RecipeIngredient { RecipeId = recipe.Id, IngredientId = rice.Id, Quantity = 300, Unit = UnitOfMeasure.Gram });

        var (user, family) = AddUserWithFamily();
        var plan = new MealPlan { FamilyId = family.Id, Name = "Week", WeekStartDate = new DateOnly(2026, 11, 2) };
        var day = new MealPlanDay { MealPlanId = plan.Id, Date = plan.WeekStartDate };
        _db.MealPlans.Add(plan);
        _db.MealPlanDays.Add(day);
        _db.MealPlanEntries.Add(new MealPlanEntry { MealPlanDayId = day.Id, RecipeId = recipe.Id, MealType = MealType.Dinner, PlannedServings = 4 });
        _db.SaveChanges();

        var generated = await Lists("en").GenerateFromMealPlanAsync(user.Id, plan.Id);
        _db.ChangeTracker.Clear();

        // The list name is a key + date, rendered in the reader's language by the client
        Assert.Equal("List.Name.Weekly::Date:2026-11-02", generated.Name);

        var armenian = await Lists("hy").GetByIdAsync(user.Id, generated.Id);
        var items = armenian!.Groups.SelectMany(g => g.Items).ToList();

        var garlicItem = items.Single(i => i.IngredientId == garlic.Id);
        Assert.Equal("Սխտոր", garlicItem.DisplayName);
        Assert.Equal(new[] { "Սխտորով բրինձ" }, garlicItem.SourceRecipes);

        // Missing Armenian name → English, not the snapshot language by accident
        Assert.Equal("Rice", items.Single(i => i.IngredientId == rice.Id).DisplayName);
    }

    [Fact]
    public async Task Hand_added_and_old_items_keep_their_stored_text()
    {
        var (user, family) = AddUserWithFamily();
        var list = new ShoppingList { FamilyId = family.Id, Name = "List" };
        _db.ShoppingLists.Add(list);
        _db.ShoppingListItems.AddRange(
            // typed by the user — no ingredient to translate
            new ShoppingListItem { ShoppingListId = list.Id, DisplayName = "Kitchen roll" },
            // generated before SourceRecipeIds existed — only the snapshot titles are known
            new ShoppingListItem { ShoppingListId = list.Id, DisplayName = "Lemons", SourceRecipes = new() { "Lemon cake" } });
        _db.SaveChanges();

        var read = await Lists("hy").GetByIdAsync(user.Id, list.Id);
        var items = read!.Groups.SelectMany(g => g.Items).ToList();

        Assert.Contains(items, i => i.DisplayName == "Kitchen roll");
        Assert.Equal(new[] { "Lemon cake" }, items.Single(i => i.DisplayName == "Lemons").SourceRecipes);
    }
}
