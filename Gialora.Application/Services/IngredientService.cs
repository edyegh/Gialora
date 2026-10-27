// Gialora.Application/Services/IngredientService.cs
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared;
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
    private readonly IContentLanguage _language;
    private readonly ILogger<IngredientService> _logger;

    public IngredientService(GialoraDbContext db, IContentLanguage language, ILogger<IngredientService> logger)
    {
        _db = db;
        _language = language;
        _logger = logger;
    }

    public async Task<List<IngredientDto>> GetAllAsync(string? search)
    {
        var lang = _language.Code;
        var query = _db.Ingredients.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Ցանկացած լեզվով — admin-ը "սխտոր"-ը գտնում է անգլերեն UI-ից էլ
            var term = search.Trim();
            query = query.Where(i => i.Translations.Any(t => EF.Functions.Like(t.Name, $"%{term}%")));
        }

        return await query
            .OrderBy(i => i.Category)
            .ThenBy(i => i.Translations
                .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
                .ThenBy(t => t.Language)
                .Select(t => t.Name)
                .FirstOrDefault())
            .Select(Projection(lang))
            .ToListAsync();
    }

    public Task<IngredientDto?> GetByIdAsync(Guid id) =>
        _db.Ingredients.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(Projection(_language.Code))
            .FirstOrDefaultAsync();

    public async Task<IngredientDto> CreateAsync(IngredientCreateDto dto)
    {
        var language = ContentLanguages.Normalize(dto.Language);
        var name = dto.Name.Trim();

        await EnsureNameIsFreeAsync(language, name, exceptId: null);

        var ingredient = new Ingredient();
        Apply(ingredient, dto);

        _db.Ingredients.Add(ingredient);
        _db.IngredientTranslations.Add(new IngredientTranslation
        {
            IngredientId = ingredient.Id,
            Language = language,
            Name = name
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation("Ingredient {IngredientId} created: {Name}", ingredient.Id, name);
        return Map(ingredient, name, language, 0);
    }

    public async Task<IngredientDto?> UpdateAsync(Guid id, IngredientUpdateDto dto)
    {
        var ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Id == id);
        if (ingredient is null)
            return null;

        var language = ContentLanguages.Normalize(dto.Language);
        var name = dto.Name.Trim();
        await EnsureNameIsFreeAsync(language, name, exceptId: id);

        // Գրում ենք ՄԻԱՅՆ այս լեզվի անունը — մյուս թարգմանությունները մնում են
        var translation = await _db.IngredientTranslations
            .FirstOrDefaultAsync(t => t.IngredientId == id && t.Language == language);

        if (translation is null)
            _db.IngredientTranslations.Add(new IngredientTranslation { IngredientId = id, Language = language, Name = name });
        else
            translation.Name = name;

        Apply(ingredient, dto);
        await _db.SaveChangesAsync();

        var usage = await _db.RecipeIngredients.CountAsync(ri => ri.IngredientId == id);
        return Map(ingredient, name, language, usage);
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

        // Անունների unique index-ը ծնողի IsDeleted-ը չի տեսնում, ուստի ջնջված
        // բաղադրիչի թարգմանությունները հանում ենք — այլապես նույն անունով նոր
        // բաղադրիչ ստեղծելը կընկներ unique index-ի վրա։
        _db.IngredientTranslations.RemoveRange(
            await _db.IngredientTranslations.Where(t => t.IngredientId == id).ToListAsync());

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<TagDto>> GetTagsAsync()
    {
        var lang = _language.Code;

        var tags = await _db.Tags.AsNoTracking()
            .Select(t => new TagDto
            {
                Id = t.Id,
                Name = t.Name,
                DisplayName = t.Translations
                    .OrderBy(tr => tr.Language == lang ? 0 : tr.Language == ContentLanguages.Default ? 1 : 2)
                    .ThenBy(tr => tr.Language)
                    .Select(tr => tr.DisplayName)
                    .FirstOrDefault() ?? t.Name,
                RecipeCount = t.RecipeTags.Count(rt => rt.Recipe.IsPublished)
            })
            .ToListAsync();

        // Դասավորում ենք հիշողության մեջ — SQL-ում նույն subquery-ն կկրկնվեր
        return tags.OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // -----------------------------------------------------------------------
    // Translations (admin)
    // -----------------------------------------------------------------------

    public async Task<List<IngredientNameDto>?> GetTranslationsAsync(Guid id)
    {
        if (!await _db.Ingredients.AnyAsync(i => i.Id == id))
            return null;

        return await _db.IngredientTranslations.AsNoTracking()
            .Where(t => t.IngredientId == id)
            .OrderBy(t => t.Language)
            .Select(t => new IngredientNameDto { Language = t.Language, Name = t.Name })
            .ToListAsync();
    }

    public async Task<bool> SaveTranslationAsync(Guid id, string language, IngredientNameDto dto)
    {
        if (!await _db.Ingredients.AnyAsync(i => i.Id == id))
            return false;

        language = RecipeService.RequireLanguage(language);
        var name = dto.Name.Trim();
        await EnsureNameIsFreeAsync(language, name, exceptId: id);

        var translation = await _db.IngredientTranslations
            .FirstOrDefaultAsync(t => t.IngredientId == id && t.Language == language);

        if (translation is null)
            _db.IngredientTranslations.Add(new IngredientTranslation { IngredientId = id, Language = language, Name = name });
        else
            translation.Name = name;

        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Վերջին անունը հանել չի կարելի — բաղադրիչը կմնար անանուն։</summary>
    public async Task<bool> DeleteTranslationAsync(Guid id, string language)
    {
        var translations = await _db.IngredientTranslations.Where(t => t.IngredientId == id).ToListAsync();
        var target = translations.FirstOrDefault(t => t.Language == language);
        if (target is null)
            return false;

        if (translations.Count == 1)
            throw new ValidationFailedException("An ingredient needs a name in at least one language.");

        _db.IngredientTranslations.Remove(target);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<TagAdminDto>> GetTagsForAdminAsync()
    {
        var tags = await _db.Tags.AsNoTracking()
            .Select(t => new TagAdminDto
            {
                Id = t.Id,
                Name = t.Name,
                RecipeCount = t.RecipeTags.Count(rt => rt.Recipe.IsPublished),
                Names = t.Translations
                    .OrderBy(tr => tr.Language)
                    .Select(tr => new TagNameDto { Language = tr.Language, DisplayName = tr.DisplayName })
                    .ToList()
            })
            .ToListAsync();

        return tags.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
    }

    public async Task<bool> SaveTagTranslationAsync(Guid tagId, string language, TagNameDto dto)
    {
        if (!await _db.Tags.AnyAsync(t => t.Id == tagId))
            return false;

        language = RecipeService.RequireLanguage(language);
        var translation = await _db.TagTranslations
            .FirstOrDefaultAsync(t => t.TagId == tagId && t.Language == language);

        if (translation is null)
            _db.TagTranslations.Add(new TagTranslation { TagId = tagId, Language = language, DisplayName = dto.DisplayName.Trim() });
        else
            translation.DisplayName = dto.DisplayName.Trim();

        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Նույն լեզվով երկու բաղադրիչ նույն անունով չի կարող լինել։ DB-ում էլ unique index կա —
    /// սա պարզապես 500-ի փոխարեն հասկանալի 409 է տալիս։
    /// </summary>
    private async Task EnsureNameIsFreeAsync(string language, string name, Guid? exceptId)
    {
        if (await _db.IngredientTranslations.AnyAsync(t =>
                t.Language == language && t.Name == name && t.IngredientId != exceptId))
            throw new ConflictException($"An ingredient named \"{name}\" already exists.");
    }

    private static void Apply(Ingredient ingredient, IngredientCreateDto dto)
    {
        ingredient.DefaultUnit = dto.DefaultUnit;
        ingredient.Category = dto.Category;
        ingredient.Allergens = dto.Allergens.Where(a => Enum.IsDefined(a)).Distinct().ToList();
        // Admin-ի ձևով պահելը = admin-ը ալերգենները ստուգեց (թեկուզ ոչ մեկը չնշեց)
        ingredient.AllergensReviewed = true;
        ingredient.IsPantryStaple = dto.IsPantryStaple;
    }

    // Պիտի լինի Expression, ոչ թե սովորական մեթոդ — EF-ը մեթոդի կանչը SQL-ի չի
    // թարգմանում և runtime-ին կնետեր "could not be translated"։
    private static Expression<Func<Ingredient, IngredientDto>> Projection(string lang) => i => new IngredientDto
    {
        Id = i.Id,
        Name = i.Translations
            .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
            .ThenBy(t => t.Language)
            .Select(t => t.Name)
            .FirstOrDefault() ?? string.Empty,
        NameLanguage = i.Translations
            .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
            .ThenBy(t => t.Language)
            .Select(t => t.Language)
            .FirstOrDefault() ?? ContentLanguages.Default,
        Languages = i.Translations.Select(t => t.Language).ToList(),
        DefaultUnit = i.DefaultUnit,
        Category = i.Category,
        Allergens = i.Allergens,
        AllergensReviewed = i.AllergensReviewed,
        IsPantryStaple = i.IsPantryStaple,
        UsedInRecipeCount = i.RecipeIngredients.Count
    };

    private static IngredientDto Map(Ingredient i, string name, string language, int usage) => new()
    {
        Id = i.Id,
        Name = name,
        NameLanguage = language,
        DefaultUnit = i.DefaultUnit,
        Category = i.Category,
        Allergens = i.Allergens,
        AllergensReviewed = i.AllergensReviewed,
        IsPantryStaple = i.IsPantryStaple,
        UsedInRecipeCount = usage
    };
}
