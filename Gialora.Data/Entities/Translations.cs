// Gialora.Data/Entities/Translations.cs
namespace Gialora.Data.Entities;

// Կատալոգի տեքստերը ապրում են առանձին աղյուսակներում՝ մեկ տող ամեն լեզվի համար։
// Ծնողը (Recipe/Ingredient/Tag) պահում է միայն լեզվից անկախ տվյալները։
//
// BaseEntity ՉԵՆ. key-ը (ParentId, Language) է, ուստի ոչ preset Guid Id կա
// (EF-ի UPDATE-ի-փոխարեն-INSERT թակարդը), ոչ սեփական soft delete — տողը ապրում
// կամ մեռնում է ծնողի հետ, իսկ query filter-ը ծնողի IsDeleted-ին է նայում։
// Ավելացնել՝ միշտ DbSet.Add-ով և FK-ն ձեռքով դրած (տես CLAUDE.md)։

public class RecipeTranslation
{
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    /// <summary>"en" | "hy" | "el" — տես ContentLanguages։</summary>
    public string Language { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Քայլերը մեկ text-ով, տողերով բաժանված (client-ը split է անում)։</summary>
    public string? Instructions { get; set; }
}

public class IngredientTranslation
{
    public Guid IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;

    public string Language { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;   // օր. "Chicken mince"
}

public class TagTranslation
{
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public string Language { get; set; } = string.Empty;

    /// <summary>Ինչ ցույց տանք UI-ում, օր. "High protein"։</summary>
    public string DisplayName { get; set; } = string.Empty;
}
