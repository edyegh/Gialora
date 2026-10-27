// Gialora.Shared/DTOs/SiteTextDtos.cs
using System.ComponentModel.DataAnnotations;

namespace Gialora.Shared.Dtos;

// Կայքի տեքստերը (գլխավոր էջ, footer, կոճակներ…) ապրում են wwwroot/i18n/*.json-ում։
// Admin-ը կարող է ցանկացած բանալու տեքստը փոխել ամեն լեզվով առանձին — փոփոխությունը
// պահվում է DB-ում և client-ի Localizer-ը այն դնում է JSON-ի արժեքի ՎՐԱ։ Ջնջելը
// նշանակում է "վերադարձնել ներկառուցված տեքստը"։

/// <summary>Admin-ի փոխած մեկ տեքստ՝ (բանալի, լեզու) զույգի համար։</summary>
public class SiteTextDto
{
    [Required, StringLength(150)]
    public string Key { get; set; } = string.Empty;

    [Required, StringLength(8)]
    public string Language { get; set; } = ContentLanguages.Default;

    [Required, StringLength(4000, MinimumLength = 1)]
    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}
