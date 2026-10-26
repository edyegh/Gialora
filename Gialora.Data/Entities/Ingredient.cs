// Gialora.Data/Entities/Ingredient.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

public class Ingredient : BaseEntity
{
    /// <summary>Անունը՝ ամեն լեզվով մեկ տող։</summary>
    public ICollection<IngredientTranslation> Translations { get; set; } = new List<IngredientTranslation>();

    /// <summary>Լռելյայն չափման միավորը — նոր RecipeIngredient ավելացնելիս prefill է արվում։</summary>
    public UnitOfMeasure DefaultUnit { get; set; } = UnitOfMeasure.Gram;

    /// <summary>Խանութի "aisle"-ը — shopping list-ը խմբավորվում է ըստ սրա (app structure §3)։</summary>
    public IngredientCategory Category { get; set; } = IngredientCategory.Other;

    /// <summary>
    /// Ինչ ալերգեններ է պարունակում՝ ԵՄ-ի 14-ից։ Admin-ը որոշում է ՄԵԿ անգամ ամեն
    /// բաղադրիչի համար ("Almond flour" → TreeNuts, "Pesto" → TreeNuts + Milk) — engine-ը
    /// անվանից ոչինչ չի գուշակում։ Պահվում է անուններով ("TreeNuts|Milk")։
    /// </summary>
    public List<Allergen> Allergens { get; set; } = new();

    /// <summary>
    /// Admin-ը ալերգենները ստուգե՞լ է։ Նոր բաղադրիչը (CSV import, migration-ից առաջ
    /// եղածները) սկսում է false-ից, և ալերգիա ունեցող ընտանիքին դրանով ռեցեպտները
    /// չեն առաջարկվում — բացակայող նշումը երբեք չպիտի վտանգավոր ճաշ բաց թողնի։
    /// </summary>
    public bool AllergensReviewed { get; set; }

    /// <summary>"Pantry staple" — աղ, ձեթ, պղպեղ. shopping list-ում կարելի է առանձնացնել։</summary>
    public bool IsPantryStaple { get; set; }

    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = new List<RecipeIngredient>();
}
