// Gialora.Data/Entities/SiteText.cs
namespace Gialora.Data.Entities;

/// <summary>
/// Admin-ի փոխած UI տեքստը՝ i18n JSON-ի բանալու վրա (օր. "Home.Heading", "hy")։
/// Ներկառուցված տեքստը մնում է JSON-ում. այս տողը միայն ծածկում է այն, իսկ
/// տողը ջնջելը վերադարձնում է ներկառուցվածը։
///
/// BaseEntity ՉԷ. key-ը (Key, Language) է, և soft delete իմաստ չունի —
/// "ջնջված" override-ը պարզապես override չէ։
/// </summary>
public class SiteText
{
    public string Key { get; set; } = string.Empty;

    /// <summary>"en" | "hy" | "el" — տես ContentLanguages։</summary>
    public string Language { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
