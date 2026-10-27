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
/// The admin edits each language separately: one PUT per language, the last language can never
/// be removed, and readers get their language or English. Covers recipes, ingredients, tags,
/// blog posts and the CSV import's _hy/_el columns.
/// </summary>
public class AdminTranslationTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly GialoraDbContext _db = new(new DbContextOptionsBuilder<GialoraDbContext>()
        .UseInMemoryDatabase($"admin-translations-{Guid.NewGuid()}")
        .Options);

    private RecipeService Recipes(string language = "en") =>
        new(_db, new FixedContentLanguage(language), NullLogger<RecipeService>.Instance);

    private IngredientService Ingredients(string language = "en") =>
        new(_db, new FixedContentLanguage(language), NullLogger<IngredientService>.Instance);

    private BlogService Blog(string language = "en") => new(_db, new FixedContentLanguage(language));

    private RecipeImportService Importer() => new(_db, NullLogger<RecipeImportService>.Instance);

    private async Task<Guid> CreateRecipeAsync(string title = "Lentil soup")
    {
        var lentils = new Ingredient();
        _db.Ingredients.Add(lentils);
        _db.IngredientTranslations.Add(new IngredientTranslation { IngredientId = lentils.Id, Language = "en", Name = "Lentils" });
        await _db.SaveChangesAsync();

        return await Recipes().CreateAsync(new RecipeCreateDto
        {
            Title = title,
            Instructions = "Simmer",
            Ingredients = new() { new() { IngredientId = lentils.Id, Quantity = 200, Unit = UnitOfMeasure.Gram } }
        }, AdminId);
    }

    // ------------------------------------------------------------ recipes

    [Fact]
    public async Task A_recipe_translation_is_added_listed_and_removed()
    {
        var id = await CreateRecipeAsync();

        await Recipes().SaveTranslationAsync(id, "hy", new RecipeTextDto { Title = "Ոսպով ապուր", Instructions = "Եփել" });

        var texts = await Recipes().GetTranslationsAsync(id);
        Assert.Equal(new[] { "en", "hy" }, texts!.Select(t => t.Language));

        var summary = (await Recipes().SearchForAdminAsync(new RecipeFilterDto(), publishedOnly: null)).Items.Single();
        Assert.Equal(new[] { "en", "hy" }, summary.Languages.Order());

        Assert.True(await Recipes().DeleteTranslationAsync(id, "hy"));
        Assert.Equal("en", Assert.Single((await Recipes().GetTranslationsAsync(id))!).Language);
    }

    [Fact]
    public async Task The_last_language_of_a_recipe_cannot_be_removed()
    {
        var id = await CreateRecipeAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => Recipes().DeleteTranslationAsync(id, "en"));
    }

    [Fact]
    public async Task Unsupported_languages_are_rejected()
    {
        var id = await CreateRecipeAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Recipes().SaveTranslationAsync(id, "fr", new RecipeTextDto { Title = "Soupe", Instructions = "Mijoter" }));
    }

    // ------------------------------------------------------------ ingredients and tags

    [Fact]
    public async Task Ingredient_names_are_edited_per_language()
    {
        var created = await Ingredients().CreateAsync(new IngredientCreateDto { Name = "Garlic" });
        var onion = await Ingredients().CreateAsync(new IngredientCreateDto { Name = "Onion" });

        await Ingredients().SaveTranslationAsync(created.Id, "hy", new IngredientNameDto { Name = "Սխտոր" });

        // The same Armenian name on a different ingredient clashes; the last name can't go
        await Assert.ThrowsAsync<ConflictException>(() =>
            Ingredients().SaveTranslationAsync(onion.Id, "hy", new IngredientNameDto { Name = "Սխտոր" }));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Ingredients().DeleteTranslationAsync(onion.Id, "en"));

        Assert.Equal("Սխտոր", (await Ingredients("hy").GetByIdAsync(created.Id))!.Name);
    }

    [Fact]
    public async Task Tag_names_follow_the_reader_language()
    {
        await CreateRecipeAsync();
        var tag = new Tag { Name = "quick" };
        _db.Tags.Add(tag);
        _db.TagTranslations.Add(new TagTranslation { TagId = tag.Id, Language = "en", DisplayName = "Quick" });
        await _db.SaveChangesAsync();

        await Ingredients().SaveTagTranslationAsync(tag.Id, "hy", new TagNameDto { DisplayName = "Արագ" });

        Assert.Equal("Արագ", (await Ingredients("hy").GetTagsAsync()).Single().DisplayName);
        Assert.Equal("Quick", (await Ingredients("el").GetTagsAsync()).Single().DisplayName);
        Assert.Equal(2, (await Ingredients().GetTagsForAdminAsync()).Single().Names.Count);
    }

    // ------------------------------------------------------------ blog

    [Fact]
    public async Task Untranslated_blog_posts_are_shown_in_english()
    {
        var author = new User { Email = "author@test.local", DisplayName = "Author" };
        _db.Users.Add(author);
        await _db.SaveChangesAsync();

        var id = await Blog().CreateAsync(new BlogPostCreateDto
        {
            Title = "Batch cooking basics",
            Content = "Cook once.",
            IsPublished = true
        }, author.Id);

        await Blog().SaveTranslationAsync(id, "hy", new BlogPostTextDto { Title = "Մեծ քանակով եփելու հիմունքներ", Content = "Եփել մեկ անգամ։" });

        var armenian = await Blog("hy").GetBySlugAsync("batch-cooking-basics");
        var greek = await Blog("el").GetBySlugAsync("batch-cooking-basics");

        Assert.Equal(("hy", "Եփել մեկ անգամ։"), (armenian!.ContentLanguage, armenian.Content));
        Assert.Equal(("en", "Batch cooking basics"), (greek!.ContentLanguage, greek.Title));
        Assert.Single((await Blog("el").GetPublishedAsync(1, 10)).Items);
        await Assert.ThrowsAsync<ValidationFailedException>(async () =>
        {
            await Blog().DeleteTranslationAsync(id, "hy");
            await Blog().DeleteTranslationAsync(id, "en");
        });
    }

    // ------------------------------------------------------------ CSV import

    [Fact]
    public async Task The_import_reads_armenian_and_greek_columns()
    {
        var result = await Importer().ImportAsync(new RecipeImportRequestDto
        {
            CsvContent =
                "recipe,instructions,ingredient,quantity,unit,recipe_hy,instructions_hy,ingredient_hy,recipe_el,ingredient_el\n" +
                "Lentil soup,Simmer,Lentils,200,Gram,Ոսպով ապուր,Եփել,Ոսպ,Φακές,Φακές\n" +
                "Lentil soup,,Onion,1,Piece,,,Սոխ,,\n",
            PublishImmediately = true
        }, AdminId);

        Assert.Empty(result.Errors);

        // Greek title without instructions_el → skipped with a warning, not a half translation
        Assert.Contains(result.Warnings, w => w.Contains("instructions_el"));

        var languages = await _db.RecipeTranslations.Select(t => t.Language).OrderBy(l => l).ToListAsync();
        Assert.Equal(new[] { "en", "hy" }, languages);

        var armenianNames = await _db.IngredientTranslations.Where(t => t.Language == "hy").Select(t => t.Name).OrderBy(n => n).ToListAsync();
        Assert.Equal(new[] { "Ոսպ", "Սոխ" }, armenianNames);
        Assert.Equal("Φακές", await _db.IngredientTranslations.Where(t => t.Language == "el").Select(t => t.Name).SingleAsync());
    }

    [Fact]
    public void The_import_template_round_trips_through_the_parser()
    {
        var template = Importer().GetTemplateCsv();
        var header = template.Split('\n')[0];

        Assert.Contains("recipe_hy", header);
        Assert.Contains("ingredient_el", header);
    }

    // ------------------------------------------------------------ plan names

    [Fact]
    public void Plan_names_are_keys_with_a_date()
    {
        var name = PlanningNotes.Encode(PlanningNotes.PlanNameWeekly, PlanningNotes.DateArg(new DateOnly(2026, 11, 2)));

        Assert.True(PlanningNotes.TryDecode(name, out var key, out var args));
        Assert.Equal(PlanningNotes.PlanNameWeekly, key);
        Assert.Equal(new[] { "Date:2026-11-02" }, args);

        // Plans saved before this change keep their English names
        Assert.False(PlanningNotes.TryDecode("Week of 26 Oct", out _, out _));
    }
}
