// Gialora.Data/Entities/BlogPost.cs
namespace Gialora.Data.Entities;

/// <summary>
/// "BLOG — Tips, guides &amp; Mediterranean inspiration" (app structure §2)։
/// Նույն backend-ը սնուցում է և՛ website-ը, և՛ app-ը (app structure §8)։
/// </summary>
public class BlogPost : BaseEntity
{
    /// <summary>ՄԵԿ է բոլոր լեզուների համար (անգլերեն վերնագրից), որ link-երը չկոտրվեն։</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Title/Excerpt/Content-ը՝ ամեն լեզվով մեկ տող։</summary>
    public ICollection<BlogPostTranslation> Translations { get; set; } = new List<BlogPostTranslation>();

    public string? CoverImageUrl { get; set; }

    public bool IsPublished { get; set; }
    public DateTime? PublishedAtUtc { get; set; }

    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;
}
