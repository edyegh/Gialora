// Gialora.Shared/DTOs/IngredientDtos.cs
using System.ComponentModel.DataAnnotations;
using Gialora.Shared.Enums;

namespace Gialora.Shared.Dtos;

public class IngredientDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public UnitOfMeasure DefaultUnit { get; set; }
    public IngredientCategory Category { get; set; }

    public bool ContainsGluten { get; set; }
    public bool ContainsDairy { get; set; }
    public bool ContainsNuts { get; set; }
    public bool ContainsEgg { get; set; }
    public bool ContainsFish { get; set; }
    public bool ContainsShellfish { get; set; }
    public bool ContainsSoy { get; set; }

    public bool IsPantryStaple { get; set; }

    /// <summary>Քանի՞ ռեցեպտում է օգտագործվում — admin-ը դրանով է հասկանում՝ կարելի՞ է ջնջել։</summary>
    public int UsedInRecipeCount { get; set; }
}

public class IngredientCreateDto
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    public UnitOfMeasure DefaultUnit { get; set; } = UnitOfMeasure.Gram;
    public IngredientCategory Category { get; set; } = IngredientCategory.Other;

    public bool ContainsGluten { get; set; }
    public bool ContainsDairy { get; set; }
    public bool ContainsNuts { get; set; }
    public bool ContainsEgg { get; set; }
    public bool ContainsFish { get; set; }
    public bool ContainsShellfish { get; set; }
    public bool ContainsSoy { get; set; }

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
