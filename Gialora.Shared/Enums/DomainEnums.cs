// Gialora.Shared/Enums/DomainEnums.cs
// Բոլոր domain enum-երը ապրում են Shared-ում, ոչ թե Data-ում։
// Պատճառը՝ Blazor client-ը reference է անում ՄԻԱՅՆ Gialora.Shared-ը։ Եթե enum-ը
// Data-ում լիներ, DTO-ն չէր կարողանա այն օգտագործել առանց ամբողջ EF Core-ը
// WebAssembly payload-ի մեջ քաշելու։
namespace Gialora.Shared.Enums;

public enum MealType
{
    Breakfast = 0,
    Lunch = 1,
    Dinner = 2,
    Snack = 3
}

public enum CuisineType
{
    Mediterranean = 0,
    Greek = 1,
    Italian = 2,
    Spanish = 3,
    MiddleEastern = 4,
    NorthAfrican = 5,
    Levantine = 6,
    Other = 99
}

public enum DifficultyLevel
{
    Easy = 0,
    Medium = 1,
    Hard = 2
}

/// <summary>Ռեցեպտի սննդային տեսակը՝ "Vegetarian/meat/fish" (app structure §A)։</summary>
public enum DietType
{
    Meat = 0,
    Fish = 1,
    Vegetarian = 2,
    Vegan = 3
}

/// <summary>
/// Հիմնական սպիտակուցը։ Սա է թույլ տալիս meal-planning-ի կանոնը՝
/// "Don't repeat the same protein two days in a row" (app structure §4)։
/// </summary>
public enum ProteinType
{
    None = 0,
    Chicken = 1,
    Beef = 2,
    Pork = 3,
    Lamb = 4,
    Fish = 5,
    Seafood = 6,
    Egg = 7,
    Legume = 8,
    Dairy = 9,
    Tofu = 10
}

/// <summary>Shopping list-ի "aisle"-ները — ըստ դրանց ենք խմբավորում գնումների ցանկը։</summary>
public enum IngredientCategory
{
    Vegetables = 0,
    Fruit = 1,
    Meat = 2,
    Fish = 3,
    DairyAndEggs = 4,
    Bakery = 5,
    Grains = 6,
    Legumes = 7,
    HerbsAndSpices = 8,
    OilsAndVinegars = 9,
    Pantry = 10,
    Frozen = 11,
    Drinks = 12,
    Other = 99
}

/// <summary>
/// Չափման միավորները։ Consolidation-ի համար դրանք բերվում են ընդհանուր base unit-ի
/// (տես Gialora.Application.Common.UnitConverter)։
/// </summary>
public enum UnitOfMeasure
{
    Gram = 0,
    Kilogram = 1,
    Milliliter = 2,
    Liter = 3,
    Piece = 4,
    Teaspoon = 5,
    Tablespoon = 6,
    Cup = 7,
    Pinch = 8,
    Bunch = 9,
    Clove = 10,
    Slice = 11,
    Can = 12,
    Pack = 13
}

/// <summary>Երեք մուտքի կետերը՝ DAILY / WEEKLY / BATCH &amp; FREEZE (app structure §2)։</summary>
public enum MealPlanType
{
    Daily = 0,
    Weekly = 1,
    Biweekly = 2,
    BatchAndFreeze = 3
}

public enum MealPlanStatus
{
    Draft = 0,
    Accepted = 1,
    Archived = 2
}

public enum FamilyMemberType
{
    Adult = 0,
    Child = 1
}

/// <summary>Feedback-ի արագ ռեակցիաները՝ 👍 / 👎 (app structure §1)։</summary>
public enum FeedbackReaction
{
    None = 0,
    Liked = 1,
    Disliked = 2
}

public enum ShoppingListStatus
{
    Active = 0,
    Completed = 1,
    Archived = 2
}

public enum BudgetLevel
{
    Any = 0,
    Low = 1,
    Medium = 2,
    High = 3
}
