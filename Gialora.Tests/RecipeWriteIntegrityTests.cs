using Gialora.Application.Common;
using Gialora.Application.Services;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gialora.Tests;

/// <summary>
/// A recipe must never be left without ingredients: an ingredient-less recipe has no
/// allergens, so the allergy hard filter would treat it as safe for everyone.
/// </summary>
public class RecipeWriteIntegrityTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly GialoraDbContext _db = new(new DbContextOptionsBuilder<GialoraDbContext>()
        .UseInMemoryDatabase($"recipe-write-{Guid.NewGuid()}")
        .Options);

    private RecipeImportService Importer() => new(_db, NullLogger<RecipeImportService>.Instance);
    private RecipeService Recipes() => new(_db, NullLogger<RecipeService>.Instance);

    private const string Header = "recipe,instructions,ingredient,quantity,unit";

    [Fact]
    public async Task Import_of_a_recipe_with_only_unknown_ingredients_saves_nothing()
    {
        var result = await Importer().ImportAsync(new RecipeImportRequestDto
        {
            CsvContent = $"{Header}\nGhost soup,Boil it,unobtainium,1,Gram\n",
            CreateMissingIngredients = false,
            PublishImmediately = true
        }, AdminId);

        Assert.Single(result.Errors);
        Assert.Empty(await _db.Recipes.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Failed_overwrite_keeps_the_existing_ingredients()
    {
        await Importer().ImportAsync(new RecipeImportRequestDto
        {
            CsvContent = $"{Header}\nLentil soup,Simmer,lentils,200,Gram\n",
            PublishImmediately = true
        }, AdminId);

        await Importer().ImportAsync(new RecipeImportRequestDto
        {
            CsvContent = $"{Header}\nLentil soup,Simmer again,unobtainium,1,Gram\n",
            CreateMissingIngredients = false,
            OverwriteExisting = true
        }, AdminId);

        var recipe = await _db.Recipes.Include(r => r.RecipeIngredients).SingleAsync();
        Assert.Single(recipe.RecipeIngredients);
        Assert.Equal("Simmer", recipe.Instructions);
    }

    [Fact]
    public async Task Titles_that_share_a_slug_in_one_batch_both_import()
    {
        var result = await Importer().ImportAsync(new RecipeImportRequestDto
        {
            CsvContent = $"{Header}\nPurée soup,Blend,carrot,1,Piece\nPuree soup,Blend,leek,1,Piece\n"
        }, AdminId);

        Assert.Empty(result.Errors);
        var slugs = await _db.Recipes.Select(r => r.Slug).ToListAsync();
        Assert.Equal(2, slugs.Distinct().Count());
    }

    [Fact]
    public async Task Update_replaces_ingredients_and_tags_in_one_save()
    {
        var flour = new Ingredient { Name = "Flour" };
        var egg = new Ingredient { Name = "Egg" };
        _db.Ingredients.AddRange(flour, egg);
        await _db.SaveChangesAsync();

        var dto = new RecipeUpdateDto
        {
            Title = "Pancakes",
            Instructions = "Mix and fry",
            Tags = new() { "breakfast", "kids" },
            Ingredients = new() { new() { IngredientId = flour.Id, Quantity = 200, Unit = UnitOfMeasure.Gram } }
        };
        var id = await Recipes().CreateAsync(dto, AdminId);

        dto.Tags = new() { "breakfast", "quick" };
        dto.Ingredients = new()
        {
            new() { IngredientId = flour.Id, Quantity = 250, Unit = UnitOfMeasure.Gram },
            new() { IngredientId = egg.Id, Quantity = 2, Unit = UnitOfMeasure.Piece }
        };
        Assert.True(await Recipes().UpdateAsync(id, dto, AdminId));

        _db.ChangeTracker.Clear();
        var recipe = await _db.Recipes
            .Include(r => r.RecipeIngredients)
            .Include(r => r.RecipeTags).ThenInclude(rt => rt.Tag)
            .SingleAsync();

        Assert.Equal(250, recipe.RecipeIngredients.Single(ri => ri.IngredientId == flour.Id).Quantity);
        Assert.Equal(2, recipe.RecipeIngredients.Count);
        Assert.Equal(new[] { "breakfast", "quick" }, recipe.RecipeTags.Select(rt => rt.Tag.Name).Order());
    }

    [Fact]
    public async Task The_same_ingredient_in_two_units_is_rejected_instead_of_dropped()
    {
        var flour = new Ingredient { Name = "Flour" };
        _db.Ingredients.Add(flour);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => Recipes().CreateAsync(new RecipeCreateDto
        {
            Title = "Bread",
            Instructions = "Bake",
            Ingredients = new()
            {
                new() { IngredientId = flour.Id, Quantity = 200, Unit = UnitOfMeasure.Gram },
                new() { IngredientId = flour.Id, Quantity = 2, Unit = UnitOfMeasure.Tablespoon }
            }
        }, AdminId));
    }
}
