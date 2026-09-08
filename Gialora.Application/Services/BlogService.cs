// Gialora.Application/Services/BlogService.cs
using Microsoft.EntityFrameworkCore;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IBlogService
{
    Task<PagedResult<BlogPostSummaryDto>> GetPublishedAsync(int page, int pageSize);
    Task<BlogPostDetailDto?> GetBySlugAsync(string slug);
    Task<List<BlogPostSummaryDto>> GetAllForAdminAsync();
    Task<BlogPostDetailDto?> GetByIdAsync(Guid id);
    Task<Guid> CreateAsync(BlogPostCreateDto dto, Guid authorId);
    Task<bool> UpdateAsync(Guid id, BlogPostUpdateDto dto);
    Task<bool> DeleteAsync(Guid id);
}

/// <summary>
/// "BLOG — Tips, guides &amp; Mediterranean inspiration" (app structure §2)։
/// Նույն backend-ից է սնվում, ինչ app-ը — ոչ մի կրկնվող CMS (app structure §8)։
/// </summary>
public class BlogService : IBlogService
{
    private readonly GialoraDbContext _db;

    public BlogService(GialoraDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<BlogPostSummaryDto>> GetPublishedAsync(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.BlogPosts.AsNoTracking().Where(p => p.IsPublished);
        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(p => p.PublishedAtUtc ?? p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(SummaryProjection)
            .ToListAsync();

        return new PagedResult<BlogPostSummaryDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    public Task<BlogPostDetailDto?> GetBySlugAsync(string slug)
    {
        var normalized = Slug.From(slug);

        return _db.BlogPosts.AsNoTracking()
            .Where(p => p.Slug == normalized && p.IsPublished)
            .Select(DetailProjection)
            .FirstOrDefaultAsync();
    }

    public Task<List<BlogPostSummaryDto>> GetAllForAdminAsync() =>
        _db.BlogPosts.AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(SummaryProjection)
            .ToListAsync();

    public Task<BlogPostDetailDto?> GetByIdAsync(Guid id) =>
        _db.BlogPosts.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(DetailProjection)
            .FirstOrDefaultAsync();

    public async Task<Guid> CreateAsync(BlogPostCreateDto dto, Guid authorId)
    {
        var post = new BlogPost
        {
            AuthorId = authorId,
            Slug = await Slug.UniqueAsync(dto.Title, s => _db.BlogPosts.AnyAsync(p => p.Slug == s))
        };

        Apply(post, dto);

        if (dto.IsPublished)
            post.PublishedAtUtc = DateTime.UtcNow;

        _db.BlogPosts.Add(post);
        await _db.SaveChangesAsync();
        return post.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, BlogPostUpdateDto dto)
    {
        var post = await _db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post is null)
            return false;

        if (!string.Equals(post.Title.Trim(), dto.Title.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            post.Slug = await Slug.UniqueAsync(dto.Title,
                s => _db.BlogPosts.AnyAsync(p => p.Slug == s && p.Id != id));
        }

        // Առաջին անգամ հրապարակելիս ամրագրում ենք ամսաթիվը, հետագա խմբագրումները
        // այն չեն փոխում — հակառակ դեպքում blog-ի հերթականությունը ամեն typo-ից կփոխվեր
        if (dto.IsPublished && post.PublishedAtUtc is null)
            post.PublishedAtUtc = DateTime.UtcNow;

        Apply(post, dto);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var post = await _db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post is null)
            return false;

        post.IsDeleted = true;
        post.IsPublished = false;
        await _db.SaveChangesAsync();
        return true;
    }

    private static void Apply(BlogPost post, BlogPostCreateDto dto)
    {
        post.Title = dto.Title.Trim();
        post.Excerpt = string.IsNullOrWhiteSpace(dto.Excerpt) ? null : dto.Excerpt.Trim();
        post.Content = dto.Content.Trim();
        post.CoverImageUrl = string.IsNullOrWhiteSpace(dto.CoverImageUrl) ? null : dto.CoverImageUrl.Trim();
        post.IsPublished = dto.IsPublished;
    }

    private static readonly System.Linq.Expressions.Expression<Func<BlogPost, BlogPostSummaryDto>>
        SummaryProjection = p => new BlogPostSummaryDto
        {
            Id = p.Id,
            Title = p.Title,
            Slug = p.Slug,
            Excerpt = p.Excerpt,
            CoverImageUrl = p.CoverImageUrl,
            AuthorName = p.Author.DisplayName,
            IsPublished = p.IsPublished,
            PublishedAtUtc = p.PublishedAtUtc
        };

    private static readonly System.Linq.Expressions.Expression<Func<BlogPost, BlogPostDetailDto>>
        DetailProjection = p => new BlogPostDetailDto
        {
            Id = p.Id,
            Title = p.Title,
            Slug = p.Slug,
            Excerpt = p.Excerpt,
            Content = p.Content,
            CoverImageUrl = p.CoverImageUrl,
            AuthorName = p.Author.DisplayName,
            IsPublished = p.IsPublished,
            PublishedAtUtc = p.PublishedAtUtc
        };
}
