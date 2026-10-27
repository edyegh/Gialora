// Gialora.Shared/DTOs/BlogDtos.cs
using System.ComponentModel.DataAnnotations;

namespace Gialora.Shared.Dtos;

public class BlogPostSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Excerpt { get; set; }
    public string? CoverImageUrl { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>Title/Excerpt/Content-ի լեզուն (fallback-ի դեպքում՝ անգլերեն կամ առկա այլ լեզու)։</summary>
    public string ContentLanguage { get; set; } = ContentLanguages.Default;

    /// <summary>Որ լեզուներով կա թարգմանություն — admin-ի ցուցակի համար։</summary>
    public List<string> Languages { get; set; } = new();
}

public class BlogPostDetailDto : BlogPostSummaryDto
{
    public string Content { get; set; } = string.Empty;
}

public class BlogPostCreateDto
{
    /// <summary>Ո՞ր լեզվի թարգմանության մեջ գրել Title/Excerpt/Content-ը։ Դատարկ → անգլերեն։</summary>
    [StringLength(8)]
    public string? Language { get; set; }

    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Excerpt { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;

    [StringLength(500)]
    public string? CoverImageUrl { get; set; }

    public bool IsPublished { get; set; }
}

public class BlogPostUpdateDto : BlogPostCreateDto
{
}
