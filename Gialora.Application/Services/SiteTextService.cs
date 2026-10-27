// Gialora.Application/Services/SiteTextService.cs
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface ISiteTextService
{
    /// <summary>Մեկ լեզվի բոլոր override-ները՝ բանալի → տեքստ (client-ի Localizer-ի համար)։</summary>
    Task<Dictionary<string, string>> GetForLanguageAsync(string language);

    /// <summary>Բոլոր լեզուների override-ները (admin-ի էջի համար)։</summary>
    Task<List<SiteTextDto>> GetAllAsync();

    Task<SiteTextDto> SaveAsync(SiteTextDto dto);

    /// <summary>Վերադարձնում է ներկառուցված տեքստը։ false = override չկար։</summary>
    Task<bool> ResetAsync(string key, string language);
}

/// <summary>
/// Admin-ի խմբագրած UI տեքստերը։ Ներկառուցված տեքստերը client-ի i18n JSON-ում են,
/// ուստի server-ը բանալիների ցուցակը չգիտի — ստուգում է միայն ձևաչափը։
/// </summary>
public class SiteTextService : ISiteTextService
{
    // "Home.Heading", "Enum.MealType.Breakfast" — նույն ձևը, ինչ i18n JSON-ի բանալիները
    private static readonly Regex KeyPattern = new(@"^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9_-]+)+$", RegexOptions.Compiled);

    private readonly GialoraDbContext _db;

    public SiteTextService(GialoraDbContext db)
    {
        _db = db;
    }

    public async Task<Dictionary<string, string>> GetForLanguageAsync(string language)
    {
        var code = RecipeService.RequireLanguage(language);

        return await _db.SiteTexts.AsNoTracking()
            .Where(t => t.Language == code)
            .ToDictionaryAsync(t => t.Key, t => t.Value);
    }

    public async Task<List<SiteTextDto>> GetAllAsync() =>
        await _db.SiteTexts.AsNoTracking()
            .OrderBy(t => t.Key).ThenBy(t => t.Language)
            .Select(t => new SiteTextDto { Key = t.Key, Language = t.Language, Value = t.Value, UpdatedAtUtc = t.UpdatedAtUtc })
            .ToListAsync();

    public async Task<SiteTextDto> SaveAsync(SiteTextDto dto)
    {
        var key = RequireKey(dto.Key);
        var language = RecipeService.RequireLanguage(dto.Language);
        var value = (dto.Value ?? string.Empty).Trim();

        if (value.Length == 0)
            throw new ValidationFailedException("The text cannot be empty — use Reset to bring back the built-in text.");

        // Մի քանի բանալի client-ում MarkupString-ով է render արվում — "<"-ը թույլ տալը
        // admin-ի մուտքագրումից XSS ուղի կբացեր։ Սովորական տեքստին "<" պետք չէ։
        if (value.Contains('<'))
            throw new ValidationFailedException("Site texts cannot contain \"<\" — HTML is not allowed.");

        var existing = await _db.SiteTexts.FirstOrDefaultAsync(t => t.Key == key && t.Language == language);
        if (existing is null)
        {
            existing = new SiteText { Key = key, Language = language };
            _db.SiteTexts.Add(existing);
        }

        existing.Value = value;
        existing.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new SiteTextDto { Key = key, Language = language, Value = value, UpdatedAtUtc = existing.UpdatedAtUtc };
    }

    public async Task<bool> ResetAsync(string key, string language)
    {
        key = RequireKey(key);
        language = RecipeService.RequireLanguage(language);

        var existing = await _db.SiteTexts.FirstOrDefaultAsync(t => t.Key == key && t.Language == language);
        if (existing is null)
            return false;

        _db.SiteTexts.Remove(existing);
        await _db.SaveChangesAsync();
        return true;
    }

    private static string RequireKey(string? key)
    {
        key = key?.Trim();
        return !string.IsNullOrEmpty(key) && key.Length <= 150 && KeyPattern.IsMatch(key)
            ? key
            : throw new ValidationFailedException($"\"{key}\" is not a valid text key.");
    }
}
