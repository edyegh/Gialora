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
}

public class BlogPostDetailDto : BlogPostSummaryDto
{
    public string Content { get; set; } = string.Empty;
}

public class BlogPostCreateDto
{
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
