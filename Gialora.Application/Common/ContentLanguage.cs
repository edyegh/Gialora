// Gialora.Application/Common/ContentLanguage.cs
using Microsoft.EntityFrameworkCore;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared;

namespace Gialora.Application.Common;

/// <summary>
/// Ընթացիկ request-ի բովանդակության լեզուն։ API-ն այն կարդում է Accept-Language
/// header-ից (Gialora.Api/Infrastructure/RequestContentLanguage), test-երը
/// օգտագործում են <see cref="FixedContentLanguage"/>։ Application-ը HttpContext
/// չի տեսնում — միայն այս interface-ը։
/// </summary>
public interface IContentLanguage
{
    /// <summary>Միշտ ContentLanguages.All-ից մեկը։</summary>
    string Code { get; }
}

public sealed class FixedContentLanguage : IContentLanguage
{
    public FixedContentLanguage(string code = ContentLanguages.Default) =>
        Code = ContentLanguages.Normalize(code);

    public string Code { get; }
}

/// <summary>
/// Լավագույն թարգմանության ընտրությունը՝ արդեն բեռնված (Include) տողերից.
/// խնդրված լեզուն → անգլերեն → առկա ցանկացածը (այբբենական, որ արդյունքը կայուն լինի)։
///
/// EF query-ների ներսում նույն կանոնը գրված է inline՝
///   .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
///   .ThenBy(t => t.Language)
/// որովհետև EF-ը մեթոդի կանչը SQL-ի չի թարգմանում։ Կանոնը փոխելիս փոխել երկուսն էլ։
/// </summary>
public static class TranslationPicker
{
    public static T? Best<T>(this IEnumerable<T> translations, string language, Func<T, string> languageOf)
        where T : class =>
        translations
            .OrderBy(t => languageOf(t) == language ? 0 : languageOf(t) == ContentLanguages.Default ? 1 : 2)
            .ThenBy(languageOf, StringComparer.Ordinal)
            .FirstOrDefault();

    public static RecipeTranslation? Best(this IEnumerable<RecipeTranslation> translations, string language) =>
        translations.Best(language, t => t.Language);

    public static IngredientTranslation? Best(this IEnumerable<IngredientTranslation> translations, string language) =>
        translations.Best(language, t => t.Language);

    public static TagTranslation? Best(this IEnumerable<TagTranslation> translations, string language) =>
        translations.Best(language, t => t.Language);

    public static string TitleIn(this Recipe recipe, string language) =>
        recipe.Translations.Best(language)?.Title ?? string.Empty;

    public static string NameIn(this Ingredient ingredient, string language) =>
        ingredient.Translations.Best(language)?.Name ?? string.Empty;

    /// <summary>
    /// Ռեցեպտների վերնագրերը՝ Id-ով, մեկ query-ով։ Ցուցակներում (feedback, admin
    /// վիճակագրություն) ավելի պարզ է, քան ամեն projection-ում fallback-ը կրկնելը։
    /// IgnoreQueryFilters — արդեն ջնջված ռեցեպտի վերնագիրն էլ պատմության մեջ պետք է։
    /// </summary>
    public static async Task<Dictionary<Guid, string>> LoadRecipeTitlesAsync(
        this GialoraDbContext db, IEnumerable<Guid> recipeIds, string language)
    {
        var ids = recipeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var rows = await db.RecipeTranslations
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => ids.Contains(t.RecipeId))
            .Select(t => new { t.RecipeId, t.Language, t.Title })
            .ToListAsync();

        return rows
            .GroupBy(r => r.RecipeId)
            .ToDictionary(g => g.Key, g => g.Best(language, r => r.Language)!.Title);
    }
}
