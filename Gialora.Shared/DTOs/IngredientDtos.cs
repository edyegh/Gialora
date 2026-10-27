// Gialora.Shared/DTOs/IngredientDtos.cs
using System.ComponentModel.DataAnnotations;
using Gialora.Shared.Enums;

namespace Gialora.Shared.Dtos;

public class IngredientDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Name-ի լեզուն (թարգմանություն չլինելու դեպքում՝ fallback-ինը)։</summary>
    public string NameLanguage { get; set; } = ContentLanguages.Default;

    /// <summary>Որ լեզուներով կա անուն — admin-ի ցուցակում "բացակայող լեզու" նշանի համար։</summary>
    public List<string> Languages { get; set; } = new();

    public UnitOfMeasure DefaultUnit { get; set; }
    public IngredientCategory Category { get; set; }

    /// <summary>Ինչ ալերգեններ է պարունակում (admin-ի որոշումը, ոչ թե անվանից գուշակված)։</summary>
    public List<Allergen> Allergens { get; set; } = new();

    /// <summary>
    /// false = admin-ը ալերգենները դեռ չի ստուգել (օր.՝ CSV import-ով ստեղծված)։
    /// Ալերգիա ունեցող ընտանիքին այդ բաղադրիչով ռեցեպտները չեն առաջարկվում։
    /// </summary>
    public bool AllergensReviewed { get; set; }

    public bool IsPantryStaple { get; set; }

    /// <summary>Քանի՞ ռեցեպտում է օգտագործվում — admin-ը դրանով է հասկանում՝ կարելի՞ է ջնջել։</summary>
    public int UsedInRecipeCount { get; set; }
}

public class IngredientCreateDto
{
    /// <summary>Name-ը ո՞ր լեզվի թարգմանության մեջ գրել։ Դատարկ → անգլերեն։</summary>
    [StringLength(8)]
    public string? Language { get; set; }

    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    public UnitOfMeasure DefaultUnit { get; set; } = UnitOfMeasure.Gram;
    public IngredientCategory Category { get; set; } = IngredientCategory.Other;

    /// <summary>Admin-ի ձևով պահելը նշանակում է՝ ալերգենները ստուգված են (AllergensReviewed = true)։</summary>
    public List<Allergen> Allergens { get; set; } = new();

    public bool IsPantryStaple { get; set; }
}

public class IngredientUpdateDto : IngredientCreateDto
{
}

public class TagDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int RecipeCount { get; set; }
}
