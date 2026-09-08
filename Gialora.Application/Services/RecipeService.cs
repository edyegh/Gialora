// Gialora.Application/Services/RecipeService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;

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

    // -----------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------

    public Task<PagedResult<RecipeSummaryDto>> SearchAsync(RecipeFilterDto filter, Guid? currentUserId) =>
        SearchCoreAsync(filter, currentUserId, publishedOnly: true);

    public Task<PagedResult<RecipeSummaryDto>> SearchForAdminAsync(RecipeFilterDto filter, bool? publishedOnly) =>
        SearchCoreAsync(filter, currentUserId: null, publishedOnly: publishedOnly);

    private async Task<PagedResult<RecipeSummaryDto>> SearchCoreAsync(
        RecipeFilterDto filter, Guid? currentUserId, bool? publishedOnly)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = BuildFilteredQuery(filter, currentUserId, publishedOnly);

        // Count-ը գնում է ՆՈՒՅՆ filter-ով, բայց առանց projection/paging-ի —
        // առանց դրա client-ը չի իմանա, թե ընդամենը քանի արդյունք կա։
        var total = await query.CountAsync();

        var items = await ApplySort(query, filter.SortBy)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(RecipeMappings.SummaryProjection(currentUserId))
            .ToListAsync();

        return new PagedResult<RecipeSummaryDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    /// <summary>
    /// Filter-ի ամբողջ logic-ը մեկ տեղում է, որովհետև meal-planning engine-ը
    /// օգտագործում է ՆՈՒՅՆ զտիչները (app structure §4)։
    /// </summary>
    private IQueryable<Recipe> BuildFilteredQuery(RecipeFilterDto filter, Guid? currentUserId, bool? publishedOnly)
    {
        var query = _db.Recipes.AsNoTracking().AsQueryable();

        if (publishedOnly == true)
            query = query.Where(r => r.IsPublished);
        else if (publishedOnly == false)
            query = query.Where(r => !r.IsPublished);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(r =>
                EF.Functions.Like(r.Title, $"%{term}%") ||
                (r.Description != null && EF.Functions.Like(r.Description, $"%{term}%")) ||
                r.RecipeIngredients.Any(ri => EF.Functions.Like(ri.Ingredient.Name, $"%{term}%")));
        }

        if (filter.MealType is { } mealType)
            query = query.Where(r => r.MealType == mealType);

        if (filter.Cuisine is { } cuisine)
            query = query.Where(r => r.Cuisine == cuisine);

        if (filter.Difficulty is { } difficulty)
            query = query.Where(r => r.Difficulty <= difficulty);

        if (filter.DietType is { } dietType)
        {
            // Vegetarian-ը ընդունում է նաև vegan ռեցեպտները, հակառակը՝ ոչ։
            query = dietType switch
            {
                Shared.Enums.DietType.Vegan =>
                    query.Where(r => r.DietType == Shared.Enums.DietType.Vegan),
                Shared.Enums.DietType.Vegetarian =>
                    query.Where(r => r.DietType == Shared.Enums.DietType.Vegetarian
                                  || r.DietType == Shared.Enums.DietType.Vegan),
                Shared.Enums.DietType.Fish =>
                    query.Where(r => r.DietType != Shared.Enums.DietType.Meat),
                _ => query
            };
        }

        if (filter.MaxTotalMinutes is { } maxMinutes and > 0)
            query = query.Where(r => r.PrepTimeMinutes + r.CookTimeMinutes <= maxMinutes);

        if (filter.SuitableForAgeMonths is { } ageMonths)
            query = query.Where(r => r.MinAgeMonths <= ageMonths);

        if (filter.FreezerFriendly == true) query = query.Where(r => r.IsFreezerFriendly);
        if (filter.LunchboxFriendly == true) query = query.Where(r => r.IsLunchboxFriendly);
        if (filter.KidFriendly == true) query = query.Where(r => r.IsKidFriendly);
        if (filter.HighProtein == true) query = query.Where(r => r.IsHighProtein);
        if (filter.IronRich == true) query = query.Where(r => r.IsIronRich);
        if (filter.BatchFriendly == true) query = query.Where(r => r.IsBatchFriendly);

        // AND — ընտրված ամեն tag պիտի առկա լինի
        foreach (var tag in filter.Tags.Select(Normalize).Where(t => t.Length > 0).Distinct())
        {
            var captured = tag;
            query = query.Where(r => r.RecipeTags.Any(rt => rt.Tag.Name == captured));
        }

        // Ալերգիա/չսիրած — բացառում ենք ամբողջ ռեցեպտը, եթե պարունակում է այս բաղադրիչը
        foreach (var ingredient in filter.ExcludeIngredients.Select(Normalize).Where(i => i.Length > 0).Distinct())
        {
            var captured = ingredient;
            query = query.Where(r => !r.RecipeIngredients.Any(ri => ri.Ingredient.Name.ToLower().Contains(captured)));
        }

        if (filter.FavoritesOnly && currentUserId is { } userId)
            query = query.Where(r => r.Favorites.Any(f => f.UserId == userId));

        return query;
    }

    private static IQueryable<Recipe> ApplySort(IQueryable<Recipe> query, RecipeSortOrder sortBy) => sortBy switch
    {
        RecipeSortOrder.Title => query.OrderBy(r => r.Title),
        RecipeSortOrder.QuickestFirst => query
            .OrderBy(r => r.PrepTimeMinutes + r.CookTimeMinutes)
            .ThenBy(r => r.Title),
        RecipeSortOrder.HighestRated => query
            .OrderByDescending(r => r.Feedbacks.Any(f => f.Rating > 0)
                ? r.Feedbacks.Where(f => f.Rating > 0).Average(f => (double)f.Rating)
                : 0d)
            .ThenBy(r => r.Title),
        _ => query.OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Title)
    };

    public async Task<RecipeDetailDto?> GetByIdAsync(Guid id, Guid? currentUserId, bool includeUnpublished = false)
    {
        var query = _db.Recipes.AsNoTracking().Where(r => r.Id == id);
        if (!includeUnpublished)
            query = query.Where(r => r.IsPublished);

        var dto = await query.Select(RecipeMappings.DetailProjection(currentUserId)).FirstOrDefaultAsync();
        return dto?.WithSteps();
    }

    public async Task<RecipeDetailDto?> GetBySlugAsync(string slug, Guid? currentUserId, bool includeUnpublished = false)
    {
        var normalized = Normalize(slug);
        var query = _db.Recipes.AsNoTracking().Where(r => r.Slug == normalized);
        if (!includeUnpublished)
            query = query.Where(r => r.IsPublished);

        var dto = await query.Select(RecipeMappings.DetailProjection(currentUserId)).FirstOrDefaultAsync();
        return dto?.WithSteps();
    }

    // -----------------------------------------------------------------------
    // Write (admin panel — app structure §11)
    // -----------------------------------------------------------------------

    public async Task<Guid> CreateAsync(RecipeCreateDto dto, Guid createdByAdminId)
    {
        await ValidateIngredientsAsync(dto.Ingredients);

        var recipe = new Recipe
        {
            Slug = await Slug.UniqueAsync(dto.Title, s => _db.Recipes.AnyAsync(r => r.Slug == s)),
            CreatedByAdminId = createdByAdminId,
            IsPublished = false
        };

        ApplyScalarFields(recipe, dto);
        recipe.RecipeIngredients = BuildIngredients(dto.Ingredients);
        recipe.RecipeTags = (await ResolveTagsAsync(dto.Tags))
            .Select(t => new RecipeTag { Tag = t })
            .ToList();

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Recipe {RecipeId} created: {Title}", recipe.Id, recipe.Title);
        return recipe.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, RecipeUpdateDto dto, Guid editedByAdminId)
    {
        var recipe = await _db.Recipes
            .Include(r => r.RecipeIngredients)
            .Include(r => r.RecipeTags)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe is null)
            return false;

        await ValidateIngredientsAsync(dto.Ingredients);

        // Վերնագիրը փոխվե՞լ է — slug-ը վերագեներացնում ենք, բայց միայն այդ դեպքում,
        // որ արդեն տարածված link-երը չկոտրվեն ամեն փոքր խմբագրումից։
        if (!string.Equals(recipe.Title.Trim(), dto.Title.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            recipe.Slug = await Slug.UniqueAsync(dto.Title,
                s => _db.Recipes.AnyAsync(r => r.Slug == s && r.Id != id));
        }

        ApplyScalarFields(recipe, dto);
        recipe.CreatedByAdminId = editedByAdminId; // audit trail — վերջին խմբագրողը

        if (dto.IsPublished && !recipe.IsPublished)
            recipe.PublishedAtUtc = DateTime.UtcNow;
        recipe.IsPublished = dto.IsPublished;

        // Join table-երը replace ենք անում ամբողջությամբ — diff անելը այստեղ
        // ավելի շատ bug է բերում, քան օգուտ։
        _db.RecipeIngredients.RemoveRange(recipe.RecipeIngredients);
        _db.RecipeTags.RemoveRange(recipe.RecipeTags);
        await _db.SaveChangesAsync();

        // FK-ը դնում ենք ձեռքով և ուղիղ AddRange ենք անում։ Tracked entity-ի
        // navigation collection-ին վերագրելը EF-ին ստիպում է լրացված key-ով
        // տողերը համարել գոյություն ունեցող և INSERT-ի փոխարեն UPDATE գեներացնել։
        var ingredients = BuildIngredients(dto.Ingredients);
        foreach (var ri in ingredients)
            ri.RecipeId = recipe.Id;
        _db.RecipeIngredients.AddRange(ingredients);

        var tags = (await ResolveTagsAsync(dto.Tags))
            .Select(t => new RecipeTag { RecipeId = recipe.Id, Tag = t })
            .ToList();
        _db.RecipeTags.AddRange(tags);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Recipe {RecipeId} updated", recipe.Id);
        return true;
    }

    public async Task<bool> SetPublishedAsync(Guid id, bool isPublished)
    {
        var recipe = await _db.Recipes.FirstOrDefaultAsync(r => r.Id == id);
        if (recipe is null)
            return false;

        if (isPublished && !recipe.IsPublished)
            recipe.PublishedAtUtc = DateTime.UtcNow;

        recipe.IsPublished = isPublished;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var recipe = await _db.Recipes.FirstOrDefaultAsync(r => r.Id == id);
        if (recipe is null)
            return false;

        // Soft delete + unpublish. Առանց unpublish-ի ջնջված ռեցեպտը կմնար
        // արդեն գեներացված meal plan-երում որպես "published"։
        recipe.IsDeleted = true;
        recipe.IsPublished = false;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Recipe {RecipeId} soft-deleted", id);
        return true;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void ApplyScalarFields(Recipe recipe, RecipeCreateDto dto)
    {
        recipe.Title = dto.Title.Trim();
        recipe.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        recipe.Instructions = dto.Instructions.Trim();
        recipe.ImageUrl = string.IsNullOrWhiteSpace(dto.ImageUrl) ? null : dto.ImageUrl.Trim();
        recipe.MealType = dto.MealType;
        recipe.Cuisine = dto.Cuisine;
        recipe.Difficulty = dto.Difficulty;
        recipe.DietType = dto.DietType;
        recipe.PrimaryProtein = dto.PrimaryProtein;
        recipe.PrepTimeMinutes = dto.PrepTimeMinutes;
        recipe.CookTimeMinutes = dto.CookTimeMinutes;
        recipe.Servings = dto.Servings;
        recipe.MinAgeMonths = dto.MinAgeMonths;
        recipe.IsFreezerFriendly = dto.IsFreezerFriendly;
        recipe.IsLunchboxFriendly = dto.IsLunchboxFriendly;
        recipe.IsKidFriendly = dto.IsKidFriendly;
        recipe.IsHighProtein = dto.IsHighProtein;
        recipe.IsIronRich = dto.IsIronRich;
        recipe.IsBatchFriendly = dto.IsBatchFriendly;
        recipe.EstimatedCostPerServing = dto.EstimatedCostPerServing;
    }

    private static List<RecipeIngredient> BuildIngredients(List<RecipeIngredientInputDto> input)
    {
        // Նույն բաղադրիչը երկու անգամ → composite primary key-ի բախում։
        // Գումարում ենք քանակները մեկ տողի մեջ (նույն միավորի դեպքում)։
        return input
            .GroupBy(i => new { i.IngredientId, i.Unit })
            .Select((g, index) => new RecipeIngredient
            {
                IngredientId = g.Key.IngredientId,
                Unit = g.Key.Unit,
                Quantity = g.Sum(x => x.Quantity),
                Note = g.Select(x => x.Note).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim(),
                IsOptional = g.All(x => x.IsOptional),
                SortOrder = g.Min(x => x.SortOrder) == 0 ? index : g.Min(x => x.SortOrder)
            })
            // Composite key-ը (RecipeId, IngredientId) է — նույն ingredient-ը երկու
            // տարբեր միավորով դեռ բախում կտա, ուստի պահում ենք առաջինը։
            .GroupBy(ri => ri.IngredientId)
            .Select(g => g.First())
            .ToList();
    }

    private async Task ValidateIngredientsAsync(List<RecipeIngredientInputDto> ingredients)
    {
        if (ingredients.Count == 0)
            throw new ValidationFailedException("A recipe needs at least one ingredient.");

        var ids = ingredients.Select(i => i.IngredientId).Distinct().ToList();
        var existing = await _db.Ingredients.CountAsync(i => ids.Contains(i.Id));

        if (existing != ids.Count)
            throw new ValidationFailedException("One or more ingredient IDs do not exist.");
    }

    /// <summary>Գոյություն ունեցող tag-երը վերաօգտագործում ենք, բացակայողները ստեղծում։</summary>
    private async Task<List<Tag>> ResolveTagsAsync(List<string> tagNames)
    {
        var normalized = tagNames
            .Select(Normalize)
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

        if (normalized.Count == 0)
            return new List<Tag>();

        // Մեկ query բոլորի համար — նախկինում ամեն tag-ի համար առանձին DB round-trip էր
        var existing = await _db.Tags.Where(t => normalized.Contains(t.Name)).ToListAsync();
        var result = new List<Tag>(existing);

        foreach (var name in normalized.Except(existing.Select(t => t.Name)))
        {
            var tag = new Tag { Name = name, DisplayName = ToDisplayName(name) };
            _db.Tags.Add(tag);
            result.Add(tag);
        }

        return result;
    }

    internal static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '-');

    internal static string ToDisplayName(string slug)
    {
        var words = slug.Replace('-', ' ').Trim();
        return words.Length == 0 ? slug : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
