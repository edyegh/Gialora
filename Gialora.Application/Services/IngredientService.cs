// Gialora.Application/Services/IngredientService.cs
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

/// <summary>
/// Բաղադրիչների կատալոգը՝ admin panel-ի "add ingredients / create categories" մասը
/// (app structure §11)։ Ռեցեպտները հղում են ԱՅՍ ցուցակին, ոչ թե ազատ տեքստի —
/// հենց դա է թույլ տալիս shopping list-ի consolidation-ը։
/// </summary>
public class IngredientService : IIngredientService
{
    private readonly GialoraDbContext _db;
    private readonly ILogger<IngredientService> _logger;

    public IngredientService(GialoraDbContext db, ILogger<IngredientService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<IngredientDto>> GetAllAsync(string? search)
    {
        var query = _db.Ingredients.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(i => EF.Functions.Like(i.Name, $"%{term}%"));
        }

        return await query
            .OrderBy(i => i.Category)
            .ThenBy(i => i.Name)
            .Select(Projection)
            .ToListAsync();
    }

    public Task<IngredientDto?> GetByIdAsync(Guid id) =>
        _db.Ingredients.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(Projection)
            .FirstOrDefaultAsync();

    public async Task<IngredientDto> CreateAsync(IngredientCreateDto dto)
    {
        var name = dto.Name.Trim();

        if (await _db.Ingredients.AnyAsync(i => i.Name == name))
            throw new ConflictException($"An ingredient named \"{name}\" already exists.");

        var ingredient = new Ingredient { Name = name };
        Apply(ingredient, dto);

        _db.Ingredients.Add(ingredient);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Ingredient {IngredientId} created: {Name}", ingredient.Id, ingredient.Name);
        return Map(ingredient, 0);
    }

    public async Task<IngredientDto?> UpdateAsync(Guid id, IngredientUpdateDto dto)
    {
        var ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Id == id);
        if (ingredient is null)
            return null;

        var name = dto.Name.Trim();
        if (await _db.Ingredients.AnyAsync(i => i.Name == name && i.Id != id))
            throw new ConflictException($"An ingredient named \"{name}\" already exists.");

        ingredient.Name = name;
        Apply(ingredient, dto);
        await _db.SaveChangesAsync();

        var usage = await _db.RecipeIngredients.CountAsync(ri => ri.IngredientId == id);
        return Map(ingredient, usage);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Id == id);
        if (ingredient is null)
            return false;

        // Առանց այս ստուգման ջնջված բաղադրիչը ռեցեպտում կմնար "ուրվական" տողի տեսքով
        var usage = await _db.RecipeIngredients.CountAsync(ri => ri.IngredientId == id);
        if (usage > 0)
            throw new ConflictException($"This ingredient is used by {usage} recipe(s) and cannot be deleted.");

        ingredient.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    public Task<List<TagDto>> GetTagsAsync() =>
        _db.Tags.AsNoTracking()
            .OrderBy(t => t.DisplayName)
            .Select(t => new TagDto
            {
                Id = t.Id,
                Name = t.Name,
                DisplayName = t.DisplayName,
                RecipeCount = t.RecipeTags.Count(rt => rt.Recipe.IsPublished)
            })
            .ToListAsync();

    private static void Apply(Ingredient ingredient, IngredientCreateDto dto)
    {
        ingredient.DefaultUnit = dto.DefaultUnit;
        ingredient.Category = dto.Category;
        ingredient.ContainsGluten = dto.ContainsGluten;
        ingredient.ContainsDairy = dto.ContainsDairy;
        ingredient.ContainsNuts = dto.ContainsNuts;
        ingredient.ContainsEgg = dto.ContainsEgg;
        ingredient.ContainsFish = dto.ContainsFish;
        ingredient.ContainsShellfish = dto.ContainsShellfish;
        ingredient.ContainsSoy = dto.ContainsSoy;
        ingredient.IsPantryStaple = dto.IsPantryStaple;
    }

    // Պիտի լինի Expression, ոչ թե սովորական մեթոդ — EF-ը մեթոդի կանչը SQL-ի չի
    // թարգմանում և runtime-ին կնետեր "could not be translated"։
    private static readonly Expression<Func<Ingredient, IngredientDto>> Projection = i => new IngredientDto
    {
        Id = i.Id,
        Name = i.Name,
        DefaultUnit = i.DefaultUnit,
        Category = i.Category,
        ContainsGluten = i.ContainsGluten,
        ContainsDairy = i.ContainsDairy,
        ContainsNuts = i.ContainsNuts,
        ContainsEgg = i.ContainsEgg,
        ContainsFish = i.ContainsFish,
        ContainsShellfish = i.ContainsShellfish,
        ContainsSoy = i.ContainsSoy,
        IsPantryStaple = i.IsPantryStaple,
        UsedInRecipeCount = i.RecipeIngredients.Count
    };

    private static IngredientDto Map(Ingredient i, int usage)
    {
        var dto = new IngredientDto
        {
            Id = i.Id,
            Name = i.Name,
            DefaultUnit = i.DefaultUnit,
            Category = i.Category,
            ContainsGluten = i.ContainsGluten,
            ContainsDairy = i.ContainsDairy,
            ContainsNuts = i.ContainsNuts,
            ContainsEgg = i.ContainsEgg,
            ContainsFish = i.ContainsFish,
            ContainsShellfish = i.ContainsShellfish,
            ContainsSoy = i.ContainsSoy,
            IsPantryStaple = i.IsPantryStaple,
            UsedInRecipeCount = usage
        };
        return dto;
    }
}
