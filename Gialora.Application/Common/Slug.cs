// Gialora.Application/Common/Slug.cs
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Gialora.Application.Common;

/// <summary>
/// URL-friendly slug-երը պետք են SEO-ի համար (app structure §8՝ website + app նույն backend-ից)։
/// </summary>
public static partial class Slug
{
    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex NonSlugChars();

    [GeneratedRegex(@"[\s-]+")]
    private static partial Regex Separators();

    public static string From(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Guid.NewGuid().ToString("n")[..8];

        // Շեշտերը հանում ենք՝ "Purée" → "puree"
        var normalized = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        var slug = builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        slug = NonSlugChars().Replace(slug, string.Empty);
        slug = Separators().Replace(slug, "-").Trim('-');

        if (slug.Length > 200)
            slug = slug[..200].Trim('-');

        // Ոչ-լատինական վերնագիրը (հայերեն, հունարեն) կարող է ամբողջովին զտվել
        return string.IsNullOrEmpty(slug) ? Guid.NewGuid().ToString("n")[..8] : slug;
    }

    /// <summary>
    /// Ապահովում է եզակիությունը՝ "chicken-meatballs", "chicken-meatballs-2", ...
    /// <paramref name="exists"/>-ը ստուգում է DB-ում առկայությունը։
    /// </summary>
    public static async Task<string> UniqueAsync(string input, Func<string, Task<bool>> exists)
    {
        var baseSlug = From(input);
        var candidate = baseSlug;
        var suffix = 2;

        while (await exists(candidate))
        {
            candidate = $"{baseSlug}-{suffix}";
            suffix++;

            if (suffix > 1000) // paranoia guard — անվերջ ցիկլ չգցենք
                // Base-ը կրճատում ենք, ոչ թե ամբողջը — [..64]-ը կարճ string-ի վրա նետում էր,
                // իսկ երկարի վրա կտրում էր հենց եզակիություն տվող GUID-ը
                return $"{baseSlug[..Math.Min(baseSlug.Length, 180)].TrimEnd('-')}-{Guid.NewGuid():n}";
        }

        return candidate;
    }
}
