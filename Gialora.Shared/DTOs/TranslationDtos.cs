// Gialora.Shared/DTOs/TranslationDtos.cs
using System.ComponentModel.DataAnnotations;

namespace Gialora.Shared.Dtos;

// Admin-ի թարգմանությունների խմբագրիչը՝ մեկ DTO ամեն լեզվի մեկ տողի համար։
// GET-ը վերադարձնում է առկա բոլոր լեզուները, PUT-ը գրում/թարմացնում է մեկը,
// DELETE-ը հանում է մեկը (վերջինը հանել չի թույլատրվում)։ Language-ը URL-ից է գալիս։

public class RecipeTextDto
{
    public string Language { get; set; } = ContentLanguages.Default;

    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required]
    public string Instructions { get; set; } = string.Empty;
}

public class IngredientNameDto
{
    public string Language { get; set; } = ContentLanguages.Default;

    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;
}

public class TagNameDto
{
    public string Language { get; set; } = ContentLanguages.Default;

    [Required, StringLength(60, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>Tag-ը admin-ի համար՝ slug + ցուցադրվող անունը ամեն առկա լեզվով։</summary>
public class TagAdminDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int RecipeCount { get; set; }
    public List<TagNameDto> Names { get; set; } = new();
}

public class BlogPostTextDto
{
    public string Language { get; set; } = ContentLanguages.Default;

    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Excerpt { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;
}
