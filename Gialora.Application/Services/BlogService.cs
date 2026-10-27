// Gialora.Application/Services/BlogService.cs
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared;
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

    /// <summary>Բոլոր լեզուների տեքստերը (admin-ի tab-երի համար)։ null = գրառում չկա։</summary>
    Task<List<BlogPostTextDto>?> GetTranslationsAsync(Guid id);
    Task<bool> SaveTranslationAsync(Guid id, string language, BlogPostTextDto dto);
    Task<bool> DeleteTranslationAsync(Guid id, string language);
}

/// <summary>
/// "BLOG — Tips, guides &amp; Mediterranean inspiration" (app structure §2)։
/// Նույն backend-ից է սնվում, ինչ app-ը — ոչ մի կրկնվող CMS (app structure §8)։
/// Տեքստերը ամեն լեզվով առանձին են. չթարգմանված գրառումը ցույց է տրվում
/// անգլերենով (նույն կանոնը, ինչ ռեցեպտներինը), ոչ թե թաքցվում։
/// </summary>
public class BlogService : IBlogService
{
    private readonly GialoraDbContext _db;
    private readonly IContentLanguage _language;

    public BlogService(GialoraDbContext db, IContentLanguage language)
    {
        _db = db;
        _language = language;
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
            .Select(SummaryProjection(_language.Code))
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
            .Select(DetailProjection(_language.Code))
            .FirstOrDefaultAsync();
    }

    public Task<List<BlogPostSummaryDto>> GetAllForAdminAsync() =>
        _db.BlogPosts.AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(SummaryProjection(_language.Code))
            .ToListAsync();

    public Task<BlogPostDetailDto?> GetByIdAsync(Guid id) =>
        _db.BlogPosts.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(DetailProjection(_language.Code))
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
        _db.BlogPostTranslations.Add(BuildText(post.Id, ContentLanguages.Normalize(dto.Language), dto.Title, dto.Excerpt, dto.Content));
        await _db.SaveChangesAsync();
        return post.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, BlogPostUpdateDto dto)
    {
        var post = await _db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post is null)
            return false;

        // Առաջին անգամ հրապարակելիս ամրագրում ենք ամսաթիվը, հետագա խմբագրումները
        // այն չեն փոխում — հակառակ դեպքում blog-ի հերթականությունը ամեն typo-ից կփոխվեր
        if (dto.IsPublished && post.PublishedAtUtc is null)
            post.PublishedAtUtc = DateTime.UtcNow;

        Apply(post, dto);

        // Գրում ենք ՄԻԱՅՆ dto.Language-ի տեքստը — մյուս լեզուներինը մնում են
        await WriteTextAsync(post, BuildText(id, ContentLanguages.Normalize(dto.Language), dto.Title, dto.Excerpt, dto.Content));
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

    // -----------------------------------------------------------------------
    // Translations (admin-ի լեզուների tab-երը)
    // -----------------------------------------------------------------------

    public async Task<List<BlogPostTextDto>?> GetTranslationsAsync(Guid id)
    {
        if (!await _db.BlogPosts.AnyAsync(p => p.Id == id))
            return null;

        return await _db.BlogPostTranslations.AsNoTracking()
            .Where(t => t.BlogPostId == id)
            .OrderBy(t => t.Language)
            .Select(t => new BlogPostTextDto
            {
                Language = t.Language,
                Title = t.Title,
                Excerpt = t.Excerpt,
                Content = t.Content
            })
            .ToListAsync();
    }

    public async Task<bool> SaveTranslationAsync(Guid id, string language, BlogPostTextDto dto)
    {
        var post = await _db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post is null)
            return false;

        await WriteTextAsync(post, BuildText(id, RecipeService.RequireLanguage(language), dto.Title, dto.Excerpt, dto.Content));
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Վերջին թարգմանությունը հանել չի կարելի — գրառումը կմնար առանց վերնագրի։</summary>
    public async Task<bool> DeleteTranslationAsync(Guid id, string language)
    {
        var translations = await _db.BlogPostTranslations.Where(t => t.BlogPostId == id).ToListAsync();
        var target = translations.FirstOrDefault(t => t.Language == language);
        if (target is null)
            return false;

        if (translations.Count == 1)
            throw new ValidationFailedException("A post needs at least one language.");

        _db.BlogPostTranslations.Remove(target);
        await _db.SaveChangesAsync();
        return true;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Մեկ լեզվի տեքստը՝ ավելացնել կամ թարմացնել (SaveChanges-ը կանչողինն է)։ Slug-ը
    /// վերագեներացվում է միայն ԱՆԳԼԵՐԵՆ վերնագիրը փոխվելիս — նույն կանոնը, ինչ ռեցեպտինը։
    /// </summary>
    private async Task WriteTextAsync(BlogPost post, BlogPostTranslation wanted)
    {
        var text = await _db.BlogPostTranslations
            .FirstOrDefaultAsync(t => t.BlogPostId == post.Id && t.Language == wanted.Language);

        if (wanted.Language == ContentLanguages.Default &&
            !string.Equals(text?.Title.Trim(), wanted.Title, StringComparison.OrdinalIgnoreCase))
        {
            post.Slug = await Slug.UniqueAsync(wanted.Title,
                s => _db.BlogPosts.AnyAsync(p => p.Slug == s && p.Id != post.Id));
        }

        if (text is null)
        {
            _db.BlogPostTranslations.Add(wanted);
        }
        else
        {
            text.Title = wanted.Title;
            text.Excerpt = wanted.Excerpt;
            text.Content = wanted.Content;
        }
    }

    private static BlogPostTranslation BuildText(Guid postId, string language, string title, string? excerpt, string content) => new()
    {
        BlogPostId = postId,
        Language = language,
        Title = title.Trim(),
        Excerpt = string.IsNullOrWhiteSpace(excerpt) ? null : excerpt.Trim(),
        Content = content.Trim()
    };

    /// <summary>Լեզվից անկախ դաշտերը (տեքստերը BuildText-ում են)։</summary>
    private static void Apply(BlogPost post, BlogPostCreateDto dto)
    {
        post.CoverImageUrl = string.IsNullOrWhiteSpace(dto.CoverImageUrl) ? null : dto.CoverImageUrl.Trim();
        post.IsPublished = dto.IsPublished;
    }

    // Լավագույն թարգմանության ՄԵԿ տողից (խնդրված → en → առկա) — նույն կանոնը,
    // ինչ RecipeMappings-ում և TranslationPicker-ում։
    private static Expression<Func<BlogPost, BlogPostSummaryDto>> SummaryProjection(string lang) =>
        p => new BlogPostSummaryDto
        {
            Id = p.Id,
            Title = p.Translations
                .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
                .ThenBy(t => t.Language)
                .Select(t => t.Title).FirstOrDefault() ?? string.Empty,
            Excerpt = p.Translations
                .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
                .ThenBy(t => t.Language)
                .Select(t => t.Excerpt).FirstOrDefault(),
            ContentLanguage = p.Translations
                .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
                .ThenBy(t => t.Language)
                .Select(t => t.Language).FirstOrDefault() ?? ContentLanguages.Default,
            Languages = p.Translations.Select(t => t.Language).ToList(),
            Slug = p.Slug,
            CoverImageUrl = p.CoverImageUrl,
            AuthorName = p.Author.DisplayName,
            IsPublished = p.IsPublished,
            PublishedAtUtc = p.PublishedAtUtc
        };

    private static Expression<Func<BlogPost, BlogPostDetailDto>> DetailProjection(string lang) =>
        p => new BlogPostDetailDto
        {
            Id = p.Id,
            Title = p.Translations
                .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
                .ThenBy(t => t.Language)
                .Select(t => t.Title).FirstOrDefault() ?? string.Empty,
            Excerpt = p.Translations
                .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
                .ThenBy(t => t.Language)
                .Select(t => t.Excerpt).FirstOrDefault(),
            Content = p.Translations
                .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
                .ThenBy(t => t.Language)
                .Select(t => t.Content).FirstOrDefault() ?? string.Empty,
            ContentLanguage = p.Translations
                .OrderBy(t => t.Language == lang ? 0 : t.Language == ContentLanguages.Default ? 1 : 2)
                .ThenBy(t => t.Language)
                .Select(t => t.Language).FirstOrDefault() ?? ContentLanguages.Default,
            Languages = p.Translations.Select(t => t.Language).ToList(),
            Slug = p.Slug,
            CoverImageUrl = p.CoverImageUrl,
            AuthorName = p.Author.DisplayName,
            IsPublished = p.IsPublished,
            PublishedAtUtc = p.PublishedAtUtc
        };
}
