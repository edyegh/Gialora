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
/// Catalogue text lives in per-language translation rows. Reads pick the requested
/// language, then English, then any language — always ALL fields from one row, so a
/// page never mixes an Armenian title with an English description. Writes touch only
/// the language they were made in.
/// </summary>
public class TranslationTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly GialoraDbContext _db = new(new DbContextOptionsBuilder<GialoraDbContext>()
        .UseInMemoryDatabase($"translations-{Guid.NewGuid()}")
        .Options);

    private RecipeService Recipes(string language = "en") =>
        new(_db, new FixedContentLanguage(language), NullLogger<RecipeService>.Instance);

    private IngredientService Ingredients(string language = "en") =>
        new(_db, new FixedContentLanguage(language), NullLogger<IngredientService>.Instance);

    private Recipe AddRecipe(string slug, params (string Language, string Title, string? Description)[] texts)
    {
        var recipe = new Recipe { Slug = slug, CreatedByAdminId = AdminId, IsPublished = true };
        _db.Recipes.Add(recipe);

        foreach (var (language, title, description) in texts)
        {
            _db.RecipeTranslations.Add(new RecipeTranslation
            {
                RecipeId = recipe.Id,
                Language = language,
                Title = title,
                Description = description,
                Instructions = $"{title} step"
            });
        }

        _db.SaveChanges();
        return recipe;
    }

    private Ingredient AddIngredient(params (string Language, string Name)[] names)
    {
        var ingredient = new Ingredient { AllergensReviewed = true };
        _db.Ingredients.Add(ingredient);

        foreach (var (language, name) in names)
            _db.IngredientTranslations.Add(new IngredientTranslation { IngredientId = ingredient.Id, Language = language, Name = name });

        _db.SaveChanges();
        return ingredient;
    }

    [Theory]
    [InlineData("hy", "Ոսպով ապուր", "hy")]   // requested language exists
    [InlineData("el", "Lentil soup", "en")]   // missing → English
    [InlineData("en", "Lentil soup", "en")]
    public async Task Recipe_text_comes_from_the_requested_language_or_english(string requested, string title, string from)
    {
        AddRecipe("lentil-soup", ("en", "Lentil soup", "Warming"), ("hy", "Ոսպով ապուր", null));

        var dto = await Recipes(requested).GetBySlugAsync("lentil-soup", currentUserId: null);

        Assert.NotNull(dto);
        Assert.Equal(title, dto.Title);
        Assert.Equal(from, dto.ContentLanguage);
    }

    [Fact]
    public async Task All_fields_come_from_one_translation_row()
    {
        // The Armenian row has no description — it must NOT borrow the English one
        AddRecipe("lentil-soup", ("en", "Lentil soup", "Warming"), ("hy", "Ոսպով ապուր", null));

        var dto = await Recipes("hy").GetBySlugAsync("lentil-soup", currentUserId: null);

        Assert.Null(dto!.Description);
        Assert.Equal(new[] { "Ոսպով ապուր step" }, dto.Steps);
    }

    [Fact]
    public async Task A_recipe_without_english_falls_back_to_any_language()
    {
        AddRecipe("fasolada", ("el", "Φασολάδα", null));

        var dto = await Recipes("hy").GetBySlugAsync("fasolada", currentUserId: null);

        Assert.Equal("Φασολάδα", dto!.Title);
        Assert.Equal("el", dto.ContentLanguage);
    }

    [Fact]
    public async Task Ingredient_names_in_a_recipe_follow_the_language()
    {
        var garlic = AddIngredient(("en", "Garlic"), ("hy", "Սխտոր"));
        var recipe = AddRecipe("garlic-bread", ("en", "Garlic bread", null));
        _db.RecipeIngredients.Add(new RecipeIngredient { RecipeId = recipe.Id, IngredientId = garlic.Id, Quantity = 2, Unit = UnitOfMeasure.Piece });
        _db.SaveChanges();

        var armenian = await Recipes("hy").GetBySlugAsync("garlic-bread", currentUserId: null);
        var greek = await Recipes("el").GetBySlugAsync("garlic-bread", currentUserId: null);

        Assert.Equal("Սխտոր", Assert.Single(armenian!.Ingredients).IngredientName);
        Assert.Equal("Garlic", Assert.Single(greek!.Ingredients).IngredientName);
    }

    [Fact]
    public async Task Saving_a_translation_leaves_the_other_languages_and_the_slug_alone()
    {
        var flour = AddIngredient(("en", "Flour"));
        var id = await Recipes().CreateAsync(new RecipeCreateDto
        {
            Title = "Pancakes",
            Instructions = "Mix and fry",
            Ingredients = new() { new() { IngredientId = flour.Id, Quantity = 200, Unit = UnitOfMeasure.Gram } }
        }, AdminId);

        var slug = (await _db.Recipes.SingleAsync()).Slug;

        await Recipes("hy").UpdateAsync(id, new RecipeUpdateDto
        {
            Language = "hy",
            Title = "Նրբաբլիթներ",
            Instructions = "Խառնել և տապակել",
            Ingredients = new() { new() { IngredientId = flour.Id, Quantity = 200, Unit = UnitOfMeasure.Gram } }
        }, AdminId);

        _db.ChangeTracker.Clear();
        var translations = await _db.RecipeTranslations.OrderBy(t => t.Language).ToListAsync();

        Assert.Equal(new[] { "en", "hy" }, translations.Select(t => t.Language));
        Assert.Equal("Pancakes", translations[0].Title);
        Assert.Equal("Նրբաբլիթներ", translations[1].Title);
        Assert.Equal(slug, (await _db.Recipes.SingleAsync()).Slug);
    }

    [Fact]
    public async Task Renaming_the_english_title_regenerates_the_slug()
    {
        var flour = AddIngredient(("en", "Flour"));
        var dto = new RecipeUpdateDto
        {
            Title = "Pancakes",
            Instructions = "Mix and fry",
            Ingredients = new() { new() { IngredientId = flour.Id, Quantity = 200, Unit = UnitOfMeasure.Gram } }
        };
        var id = await Recipes().CreateAsync(dto, AdminId);

        dto.Title = "Fluffy pancakes";
        await Recipes().UpdateAsync(id, dto, AdminId);

        Assert.Equal("fluffy-pancakes", (await _db.Recipes.SingleAsync()).Slug);
    }

    [Fact]
    public async Task Ingredient_names_are_unique_per_language_only()
    {
        AddIngredient(("en", "Feta"));

        // Same spelling in another language is a different translation, not a clash
        await Ingredients().CreateAsync(new IngredientCreateDto { Language = "el", Name = "Feta" });

        await Assert.ThrowsAsync<ConflictException>(() =>
            Ingredients().CreateAsync(new IngredientCreateDto { Language = "en", Name = "Feta" }));
    }

    [Fact]
    public async Task Deleting_an_ingredient_frees_its_names()
    {
        var mint = AddIngredient(("en", "Mint"), ("hy", "Անանուխ"));

        Assert.True(await Ingredients().DeleteAsync(mint.Id));
        Assert.Empty(await _db.IngredientTranslations.IgnoreQueryFilters().ToListAsync());

        // The unique (Language, Name) index would otherwise reject this
        var again = await Ingredients().CreateAsync(new IngredientCreateDto { Name = "Mint" });
        Assert.Equal("Mint", again.Name);
    }

    [Fact]
    public async Task Editing_an_ingredient_writes_only_its_language()
    {
        var garlic = AddIngredient(("en", "Garlic"));

        await Ingredients("hy").UpdateAsync(garlic.Id, new IngredientUpdateDto { Language = "hy", Name = "Սխտոր" });

        Assert.Equal("Garlic", (await Ingredients("en").GetByIdAsync(garlic.Id))!.Name);
        var armenian = await Ingredients("hy").GetByIdAsync(garlic.Id);
        Assert.Equal("Սխտոր", armenian!.Name);
        Assert.Equal("hy", armenian.NameLanguage);
    }

    [Theory]
    [InlineData("hy-AM", "hy")]
    [InlineData("EL", "el")]
    [InlineData("fr", "en")]
    [InlineData(null, "en")]
    public void Language_codes_are_normalized(string? input, string expected) =>
        Assert.Equal(expected, ContentLanguages.Normalize(input));
}
