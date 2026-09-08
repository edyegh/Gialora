// Gialora.Data/Entities/BlogPost.cs
namespace Gialora.Data.Entities;

/// <summary>
/// "BLOG — Tips, guides &amp; Mediterranean inspiration" (app structure §2)։
/// Նույն backend-ը սնուցում է և՛ website-ը, և՛ app-ը (app structure §8)։
/// </summary>
public class BlogPost : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Excerpt { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }

    public bool IsPublished { get; set; }
    public DateTime? PublishedAtUtc { get; set; }

    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;
}
