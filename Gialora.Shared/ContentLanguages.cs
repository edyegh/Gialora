// Gialora.Shared/ContentLanguages.cs
namespace Gialora.Shared;

/// <summary>
/// Կատալոգի բովանդակության լեզուները (ռեցեպտ, բաղադրիչ, tag)։ Shared-ում է, որ
/// API-ն և client-ը նույն ցուցակը տեսնեն։ Թարգմանություն չունեցող դաշտը
/// ետ է ընկնում անգլերենին, հետո՝ առկա ցանկացած լեզվին։
/// </summary>
public static class ContentLanguages
{
    public const string Default = "en";

    public static readonly IReadOnlyList<string> All = new[] { "en", "hy", "el" };

    public static bool IsSupported(string? code) =>
        code is not null && All.Contains(code);

    /// <summary>"hy-AM" → "hy", անհայտ/դատարկ → "en"։</summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Default;

        var primary = code.Trim().Split('-', '_')[0].ToLowerInvariant();
        return IsSupported(primary) ? primary : Default;
    }
}
