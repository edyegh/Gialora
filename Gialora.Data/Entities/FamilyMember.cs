// Gialora.Data/Entities/FamilyMember.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

public class FamilyMember : BaseEntity
{
    public Guid FamilyId { get; set; }
    public Family Family { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>Adult / Child + age (app structure §1)։</summary>
    public FamilyMemberType MemberType { get; set; } = FamilyMemberType.Adult;

    public int? Age { get; set; }

    /// <summary>Երեխաների համար ամիսներով տարիքը ավելի ճշգրիտ է (0–3 տ.)։</summary>
    public int? AgeMonths { get; set; }

    // CSV-ով պահվող ցուցակներ (օր. ["vegetarian", "gluten-free"])
    public List<string> DietaryRestrictions { get; set; } = new();
    public List<string> Allergies { get; set; } = new();

    /// <summary>Ազատ նպատակներ՝ "more iron", "picky eater" (app structure §1)։</summary>
    public List<string> Goals { get; set; } = new();

    /// <summary>Տարիքը ամիսներով — engine-ը սրանով է ստուգում Recipe.MinAgeMonths-ը։</summary>
    public int? EffectiveAgeMonths => AgeMonths ?? (Age.HasValue ? Age.Value * 12 : null);
}
