// Gialora.Shared/Dtos/RecipeDtos.cs
using System.ComponentModel.DataAnnotations;

namespace Gialora.Shared.Dtos;

// Ինչ վերադարձնում ենք ցուցակում (light — առանց ingredients/instructions)
public class RecipeSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PrepTimeMinutes { get; set; }
    public int CookTimeMinutes { get; set; }
    public string? ImageUrl { get; set; }
    public List<string> Tags { get; set; } = new();
}

// Ինչ վերադարձնում ենք single recipe-ի համար (full detail)
public class RecipeDetailDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Instructions { get; set; }
    public int PrepTimeMinutes { get; set; }
    public int CookTimeMinutes { get; set; }
    public int Servings { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsPublished { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<RecipeIngredientDto> Ingredients { get; set; } = new();
}

public class RecipeIngredientDto
{
    public Guid IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}

// Ինչ ընդունում ենք Create/Update-ի ժամանակ — ՄԻԱՅՆ այն դաշտերը, որ իրավունք ունես փոխել client-ից
public class RecipeCreateDto
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required]
    public string Instructions { get; set; } = string.Empty;

    [Range(0, 600)]
    public int PrepTimeMinutes { get; set; }

    [Range(0, 600)]
    public int CookTimeMinutes { get; set; }

    [Range(1, 50)]
    public int Servings { get; set; } = 4;

    [Url]
    public string? ImageUrl { get; set; }

    public List<string> Tags { get; set; } = new();
    public List<RecipeIngredientInputDto> Ingredients { get; set; } = new();
}

public class RecipeIngredientInputDto
{
    [Required]
    public Guid IngredientId { get; set; }

    [Range(0.01, 10000)]
    public decimal Quantity { get; set; }
}