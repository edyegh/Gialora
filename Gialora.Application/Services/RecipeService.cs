// Gialora.Application/Services/RecipeService.cs
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;

namespace Gialora.Application.Services;

public class RecipeService : IRecipeService
{
    private readonly GialoraDbContext _db;
    private readonly IContentLanguage _language;
    private readonly ILogger<RecipeService> _logger;

    public RecipeService(GialoraDbContext db, IContentLanguage language, ILogger<RecipeService> logger)
    {
        _db = db;
        _language = language;
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

        var items = await ApplySort(query, filter.SortBy, _language.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(RecipeMappings.SummaryProjection(currentUserId, _language.Code))
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
            // Որոնում ԲՈԼՈՐ լեզուներով — "սխտոր"-ն էլ, "garlic"-ն էլ գտնում են նույն ռեցեպտը,
            // անկախ նրանից, թե UI-ն որ լեզվով է
            var term = filter.Search.Trim();
            query = query.Where(r =>
                r.Translations.Any(t =>
                    EF.Functions.Like(t.Title, $"%{term}%") ||
                    (t.Description != null && EF.Functions.Like(t.Description, $"%{term}%"))) ||
                r.RecipeIngredients.Any(ri =>
                    ri.Ingredient.Translations.Any(t => EF.Functions.Like(t.Name, $"%{term}%"))));
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
            query = query.Where(r => !r.RecipeIngredients.Any(ri =>
                ri.Ingredient.Translations.Any(t => t.Name.ToLower().Contains(captured))));
        }

        if (filter.FavoritesOnly && currentUserId is { } userId)
            query = query.Where(r => r.Favorites.Any(f => f.UserId == userId));

        return query;
    }

    private static IQueryable<Recipe> ApplySort(IQueryable<Recipe> query, RecipeSortOrder sortBy, string lang)
    {
        // Այբբենական կարգը՝ ըստ ՑՈՒՑԱԴՐՎՈՂ վերնագրի (նույն fallback-ով, ինչ projection-ում)
        Expression<Func<Recipe, string?>> title = r => r.Translations
            .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
            .ThenBy(t => t.Language)
            .Select(t => t.Title)
            .FirstOrDefault();

        return sortBy switch
        {
            RecipeSortOrder.Title => query.OrderBy(title),
            RecipeSortOrder.QuickestFirst => query
                .OrderBy(r => r.PrepTimeMinutes + r.CookTimeMinutes)
                .ThenBy(title),
            RecipeSortOrder.HighestRated => query
                .OrderByDescending(r => r.Feedbacks.Any(f => f.Rating > 0)
                    ? r.Feedbacks.Where(f => f.Rating > 0).Average(f => (double)f.Rating)
                    : 0d)
                .ThenBy(title),
            _ => query.OrderByDescending(r => r.CreatedAtUtc).ThenBy(title)
        };
    }

    public async Task<RecipeDetailDto?> GetByIdAsync(Guid id, Guid? currentUserId, bool includeUnpublished = false)
    {
        var query = _db.Recipes.AsNoTracking().Where(r => r.Id == id);
        if (!includeUnpublished)
            query = query.Where(r => r.IsPublished);

        var dto = await query.Select(RecipeMappings.DetailProjection(currentUserId, _language.Code)).FirstOrDefaultAsync();
        return dto?.WithSteps();
    }

    public async Task<RecipeDetailDto?> GetBySlugAsync(string slug, Guid? currentUserId, bool includeUnpublished = false)
    {
        var normalized = Normalize(slug);
        var query = _db.Recipes.AsNoTracking().Where(r => r.Slug == normalized);
        if (!includeUnpublished)
            query = query.Where(r => r.IsPublished);

        var dto = await query.Select(RecipeMappings.DetailProjection(currentUserId, _language.Code)).FirstOrDefaultAsync();
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
        _db.RecipeTranslations.Add(BuildTranslation(recipe.Id, dto));
        await _db.SaveChangesAsync();

        _logger.LogInformation("Recipe {RecipeId} created: {Title}", recipe.Id, dto.Title);
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

        // Գրում ենք ՄԻԱՅՆ dto.Language-ի թարգմանությունը — մյուս լեզուներինը մնում են
        await WriteTextAsync(recipe, BuildTranslation(recipe.Id, dto));

        ApplyScalarFields(recipe, dto);
        recipe.CreatedByAdminId = editedByAdminId; // audit trail — վերջին խմբագրողը

        if (dto.IsPublished && !recipe.IsPublished)
            recipe.PublishedAtUtc = DateTime.UtcNow;
        recipe.IsPublished = dto.IsPublished;

        // Join table-երը diff ենք անում և պահում ՄԵԿ SaveChanges-ով (ատոմար)։ Նախկինում
        // "ջնջել ամբողջը → save → ավելացնել → save" էր (նույն composite key-ով տողը
        // մեկ save-ում ջնջել ու ավելացնել EF-ը չի թույլատրում)։ Երկրորդ save-ի սխալը
        // թողնում էր published ռեցեպտ՝ առանց բաղադրիչների, որը ալերգիայի filter-ը անցնում էր։
        var current = recipe.RecipeIngredients.ToDictionary(ri => ri.IngredientId);
        var wanted = BuildIngredients(dto.Ingredients);

        _db.RecipeIngredients.RemoveRange(current.Values.Where(ri => wanted.All(w => w.IngredientId != ri.IngredientId)));

        foreach (var w in wanted)
        {
            if (current.TryGetValue(w.IngredientId, out var existing))
            {
                existing.Quantity = w.Quantity;
                existing.Unit = w.Unit;
                existing.Note = w.Note;
                existing.IsOptional = w.IsOptional;
                existing.SortOrder = w.SortOrder;
            }
            else
            {
                // FK-ը ձեռքով + ուղիղ Add. tracked recipe-ի collection-ին ավելացնելը
                // EF-ին ստիպում է INSERT-ի փոխարեն UPDATE գեներացնել։
                w.RecipeId = recipe.Id;
                _db.RecipeIngredients.Add(w);
            }
        }

        var wantedTags = await ResolveTagsAsync(dto.Tags);
        var wantedTagIds = wantedTags.Select(t => t.Id).ToHashSet();
        var currentTagIds = recipe.RecipeTags.Select(rt => rt.TagId).ToHashSet();

        _db.RecipeTags.RemoveRange(recipe.RecipeTags.Where(rt => !wantedTagIds.Contains(rt.TagId)));
        _db.RecipeTags.AddRange(wantedTags
            .Where(t => !currentTagIds.Contains(t.Id))
            .Select(t => new RecipeTag { RecipeId = recipe.Id, Tag = t }));

        await _db.SaveChangesAsync();

        _logger.LogInformation("Recipe {RecipeId} updated", recipe.Id);
        return true;
    }

    // -----------------------------------------------------------------------
    // Translations (admin-ի լեզուների tab-երը)
    // -----------------------------------------------------------------------

    public async Task<List<RecipeTextDto>?> GetTranslationsAsync(Guid id)
    {
        if (!await _db.Recipes.AnyAsync(r => r.Id == id))
            return null;

        return await _db.RecipeTranslations.AsNoTracking()
            .Where(t => t.RecipeId == id)
            .OrderBy(t => t.Language)
            .Select(t => new RecipeTextDto
            {
                Language = t.Language,
                Title = t.Title,
                Description = t.Description,
                Instructions = t.Instructions ?? string.Empty
            })
            .ToListAsync();
    }

    public async Task<bool> SaveTranslationAsync(Guid id, string language, RecipeTextDto dto)
    {
        var recipe = await _db.Recipes.FirstOrDefaultAsync(r => r.Id == id);
        if (recipe is null)
            return false;

        await WriteTextAsync(recipe, new RecipeTranslation
        {
            RecipeId = id,
            Language = RequireLanguage(language),
            Title = dto.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            Instructions = dto.Instructions.Trim()
        });

        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Վերջին թարգմանությունը հանել չի կարելի — ռեցեպտը կմնար առանց վերնագրի։</summary>
    public async Task<bool> DeleteTranslationAsync(Guid id, string language)
    {
        var translations = await _db.RecipeTranslations.Where(t => t.RecipeId == id).ToListAsync();
        var target = translations.FirstOrDefault(t => t.Language == language);
        if (target is null)
            return false;

        if (translations.Count == 1)
            throw new ValidationFailedException("A recipe needs at least one language.");

        _db.RecipeTranslations.Remove(target);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Մեկ լեզվի տեքստը՝ ավելացնել կամ թարմացնել (SaveChanges-ը կանչողինն է)։
    /// Slug-ը գալիս է ԱՆԳԼԵՐԵՆ վերնագրից և վերագեներացվում է միայն երբ այն փոխվում է —
    /// թարգմանություն ավելացնելը կամ փոքր խմբագրումը արդեն տարածված link-երը չի կոտրում։
    /// (Ոչ-լատինական վերնագրով ստեղծված ռեցեպտը անգլերեն վերնագիր ստանալիս կարդացվող slug է ստանում։)
    /// </summary>
    private async Task WriteTextAsync(Recipe recipe, RecipeTranslation wanted)
    {
        var text = await _db.RecipeTranslations
            .FirstOrDefaultAsync(t => t.RecipeId == recipe.Id && t.Language == wanted.Language);

        if (wanted.Language == ContentLanguages.Default &&
            !string.Equals(text?.Title.Trim(), wanted.Title, StringComparison.OrdinalIgnoreCase))
        {
            recipe.Slug = await Slug.UniqueAsync(wanted.Title,
                s => _db.Recipes.AnyAsync(r => r.Slug == s && r.Id != recipe.Id));
        }

        if (text is null)
        {
            _db.RecipeTranslations.Add(wanted);
        }
        else
        {
            text.Title = wanted.Title;
            text.Description = wanted.Description;
            text.Instructions = wanted.Instructions;
        }
    }

    internal static string RequireLanguage(string? code) =>
        ContentLanguages.IsSupported(code)
            ? code!
            : throw new ValidationFailedException($"Unsupported language \"{code}\".");

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

        // MealPlanEntry.Recipe-ը required navigation է, իսկ Recipe-ը query filter ունի —
        // Include-ը INNER JOIN է, և ջնջված ռեցեպտի ճաշերը լուռ անհետանում են ընտանիքների
        // պլաններից ու shopping list-երից (swap/lock-ն էլ 404 է տալիս)։ Ընթացիկ/ապագա
        // պլաններում օգտագործվող ռեցեպտը չենք ջնջում — unpublish-ը այն հանում է
        // planner-ից, բայց թողնում արդեն պլանավորված ճաշերում։
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var plansUsingIt = await _db.MealPlanEntries
            .Where(e => e.RecipeId == id
                && e.MealPlanDay.Date >= today
                && e.MealPlanDay.MealPlan.Status != MealPlanStatus.Archived)
            .Select(e => e.MealPlanDay.MealPlanId)
            .Distinct()
            .CountAsync();
        if (plansUsingIt > 0)
            throw new ConflictException($"This recipe is in {plansUsingIt} current or upcoming meal plan(s). Unpublish it instead.");

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

    private static RecipeTranslation BuildTranslation(Guid recipeId, RecipeCreateDto dto) => new()
    {
        RecipeId = recipeId,
        Language = ContentLanguages.Normalize(dto.Language),
        Title = dto.Title.Trim(),
        Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
        Instructions = dto.Instructions.Trim()
    };

    /// <summary>Լեզվից անկախ դաշտերը (տեքստերը BuildTranslation-ում են)։</summary>
    private static void ApplyScalarFields(Recipe recipe, RecipeCreateDto dto)
    {
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
            // Նույն ingredient-ը երկու տարբեր միավորով ValidateIngredientsAsync-ը մերժում է
            .ToList();
    }

    private async Task ValidateIngredientsAsync(List<RecipeIngredientInputDto> ingredients)
    {
        if (ingredients.Count == 0)
            throw new ValidationFailedException("A recipe needs at least one ingredient.");

        // Composite key-ը (RecipeId, IngredientId) է — "200 g ալյուր" + "2 tbsp ալյուր"
        // մեկ տող չեն կարող լինել։ Նախկինում երկրորդը լուռ կորչում էր (shopping list-ը
        // պակաս էր հաշվում) — հիմա ասում ենք admin-ին։
        if (ingredients.GroupBy(i => i.IngredientId).Any(g => g.Select(i => i.Unit).Distinct().Count() > 1))
            throw new ValidationFailedException("The same ingredient is listed in two different units. Combine them into one row.");

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
            // Նոր tag-ը ստանում է անգլերեն ցուցադրվող անուն slug-ից. թարգմանությունները՝ հետո
            var tag = new Tag { Name = name };
            _db.Tags.Add(tag);
            _db.TagTranslations.Add(new TagTranslation
            {
                TagId = tag.Id,
                Language = ContentLanguages.Default,
                DisplayName = ToDisplayName(name)
            });
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
