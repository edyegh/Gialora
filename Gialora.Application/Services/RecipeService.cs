// Gialora.Api/Services/RecipeService.cs
using Microsoft.EntityFrameworkCore;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Gialora.Application;
using Microsoft.Extensions.Logging;

namespace Gialora.Application.Services;

public class RecipeService : IRecipeService
{
    private readonly GialoraDbContext _db;
    private readonly ILogger<RecipeService> _logger;

    public RecipeService(GialoraDbContext db, ILogger<RecipeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IEnumerable<RecipeSummaryDto>> GetPublishedRecipesAsync(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        return await _db.Recipes
            .Where(r => r.IsPublished)
            .OrderBy(r => r.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new RecipeSummaryDto
            {
                Id = r.Id,
                Title = r.Title,
                Description = r.Description,
                PrepTimeMinutes = r.PrepTimeMinutes,
                CookTimeMinutes = r.CookTimeMinutes,
                ImageUrl = r.ImageUrl,
                Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList()
            })
            .ToListAsync();
    }

    public async Task<RecipeDetailDto?> GetRecipeByIdAsync(Guid id)
    {
        return await _db.Recipes
            .Where(r => r.Id == id && r.IsPublished)
            .Select(r => new RecipeDetailDto
            {
                Id = r.Id,
                Title = r.Title,
                Description = r.Description,
                Instructions = r.Instructions,
                PrepTimeMinutes = r.PrepTimeMinutes,
                CookTimeMinutes = r.CookTimeMinutes,
                Servings = r.Servings,
                ImageUrl = r.ImageUrl,
                IsPublished = r.IsPublished,
                Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
                Ingredients = r.RecipeIngredients.Select(ri => new RecipeIngredientDto
                {
                    IngredientId = ri.IngredientId,
                    IngredientName = ri.Ingredient.Name,
                    Unit = ri.Ingredient.Unit,
                    Quantity = ri.Quantity
                }).ToList()
            })
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> CreateRecipeAsync(RecipeCreateDto dto, Guid createdByAdminId)
    {
        var ingredientIds = dto.Ingredients.Select(i => i.IngredientId).ToList();
        var existingCount = await _db.Ingredients.CountAsync(i => ingredientIds.Contains(i.Id));
        if (existingCount != ingredientIds.Count)
            throw new InvalidOperationException("One or more ingredient IDs do not exist.");

        var tagEntities = new List<Tag>();
        foreach (var tagName in dto.Tags.Select(t => t.Trim().ToLowerInvariant()).Distinct())
        {
            var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
            if (tag is null)
            {
                tag = new Tag { Name = tagName };
                _db.Tags.Add(tag);
            }
            tagEntities.Add(tag);
        }

        var recipe = new Recipe
        {
            Title = dto.Title.Trim(),
            Description = dto.Description?.Trim(),
            Instructions = dto.Instructions.Trim(),
            PrepTimeMinutes = dto.PrepTimeMinutes,
            CookTimeMinutes = dto.CookTimeMinutes,
            Servings = dto.Servings,
            ImageUrl = dto.ImageUrl,
            IsPublished = false,
            CreatedByAdminId = createdByAdminId,
            RecipeIngredients = dto.Ingredients.Select(i => new RecipeIngredient
            {
                IngredientId = i.IngredientId,
                Quantity = i.Quantity
            }).ToList(),
            RecipeTags = tagEntities.Select(t => new RecipeTag { Tag = t }).ToList()
        };

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Recipe {RecipeId} created: {Title}", recipe.Id, recipe.Title);
        return recipe.Id;
    }

    public async Task<bool> DeleteRecipeAsync(Guid id)
    {
        var recipe = await _db.Recipes.FindAsync(id);
        if (recipe is null)
            return false;

        recipe.IsDeleted = true;
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }
}