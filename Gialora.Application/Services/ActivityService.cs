// Gialora.Application/Services/ActivityService.cs
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;

namespace Gialora.Application.Services;

public interface IActivityService
{
    /// <summary>Browser-ի batch-ը։ userId-ն null է հյուրի համար։</summary>
    Task RecordAsync(Guid? userId, ActivityBatchDto batch);

    Task<AdminStatsDto> GetStatsAsync(int days);

    Task<AdminUserActivityDto> GetUserActivityAsync(Guid userId, int days);
}

/// <summary>
/// Admin-ի վիճակագրությունը։ "Ինչ են հավանում" — արդեն եղած տվյալներից (FavoriteRecipe,
/// Feedback-ի 👍), "ինչ են սեղմում / անում" — UserActivity-ից, որը client-ը գրում է
/// միայն cookie-ների "Accept all"-ից հետո։
/// </summary>
public partial class ActivityService : IActivityService
{
    private const int TopListSize = 10;
    private const int RecentListSize = 100;
    private const int MaxDays = 365;

    private readonly GialoraDbContext _db;

    public ActivityService(GialoraDbContext db) => _db = db;

    public async Task RecordAsync(Guid? userId, ActivityBatchDto batch)
    {
        if (batch.Events.Count == 0)
            return;

        if (userId is { } id)
        {
            var role = await _db.Users.AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => (UserRole?)u.Role)
                .FirstOrDefaultAsync();

            // Admin-ի սեփական click-երը կաղտոտեին user-ների վիճակագրությունը
            if (role == UserRole.Admin)
                return;

            // Token-ը վավեր է, բայց user-ը ջնջված է — գրանցում ենք որպես հյուր
            if (role is null)
                userId = null;
        }

        var rows = new List<UserActivity>();

        foreach (var e in batch.Events.Take(ActivityBatchDto.MaxEvents))
        {
            if (!Enum.IsDefined(e.Type))
                continue;

            var target = NormalizeTarget(e.Target);
            var path = NormalizePath(e.Path);

            if (e.Type == ActivityType.Click && target is null)
                continue;
            if (e.Type == ActivityType.RecipeView && e.RecipeId is null)
                continue;

            rows.Add(new UserActivity
            {
                UserId = userId,
                Type = e.Type,
                Target = e.Type == ActivityType.Click ? target : null,
                Path = path,
                RecipeId = e.Type == ActivityType.RecipeView ? e.RecipeId : null
            });
        }

        if (rows.Count == 0)
            return;

        _db.UserActivities.AddRange(rows);
        await _db.SaveChangesAsync();
    }

    public async Task<AdminStatsDto> GetStatsAsync(int days)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var since = DateTime.UtcNow.AddDays(-days);

        var activity = _db.UserActivities.AsNoTracking().Where(a => a.CreatedAtUtc >= since);
        var byType = await CountByTypeAsync(activity);

        var stats = new AdminStatsDto
        {
            Days = days,
            TotalUsers = await _db.Users.CountAsync(u => u.Role == UserRole.User),
            NewUsers = await _db.Users.CountAsync(u => u.Role == UserRole.User && u.CreatedAtUtc >= since),
            ActiveUsers = await activity.Where(a => a.UserId != null).Select(a => a.UserId).Distinct().CountAsync(),
            PageViews = byType.GetValueOrDefault(ActivityType.PageView),
            Clicks = byType.GetValueOrDefault(ActivityType.Click),
            RecipeViews = byType.GetValueOrDefault(ActivityType.RecipeView),
            FavoritesAdded = await _db.FavoriteRecipes.CountAsync(f => f.CreatedAtUtc >= since),
            PlansCreated = await _db.MealPlans.CountAsync(p => p.CreatedAtUtc >= since),

            // Պահպանածն ու 👍-ը "վիճակ" են, ոչ թե իրադարձություն — հաշվում ենք ամբողջ ժամանակի համար
            MostSavedRecipes = await TopRecipesAsync(_db.FavoriteRecipes.Select(f => f.RecipeId)),
            // Նույն մարդու կրկնակի 👍-ը (մի քանի feedback նույն ռեցեպտին) մեկ անգամ է հաշվվում
            MostLikedRecipes = await TopRecipesAsync(_db.Feedbacks
                .Where(f => f.Reaction == FeedbackReaction.Liked)
                .Select(f => new { f.UserId, f.RecipeId })
                .Distinct()
                .Select(x => x.RecipeId)),
            MostViewedRecipes = await TopRecipesAsync(RecipeViewIds(activity)),
            TopClicks = await TopLabelsAsync(activity.Where(a => a.Type == ActivityType.Click).Select(a => a.Target!)),
            TopPages = await TopLabelsAsync(activity.Where(a => a.Type == ActivityType.PageView && a.Path != null).Select(a => a.Path!))
        };

        stats.Users = await UserRowsAsync(activity);
        return stats;
    }

    public async Task<AdminUserActivityDto> GetUserActivityAsync(Guid userId, int days)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var since = DateTime.UtcNow.AddDays(-days);

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.CreatedAtUtc })
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("User not found.");

        var all = _db.UserActivities.AsNoTracking().Where(a => a.UserId == userId);
        var activity = all.Where(a => a.CreatedAtUtc >= since);
        var byType = await CountByTypeAsync(activity);

        var lastActive = await all.Select(a => (DateTime?)a.CreatedAtUtc).MaxAsync();

        var saved = await _db.FavoriteRecipes.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new RecipeRefDto { Title = f.Recipe.Title, Slug = f.Recipe.Slug, AtUtc = f.CreatedAtUtc })
            .ToListAsync();

        // Նույն ռեցեպտի կրկնակի 👍-ը (մի քանի feedback) մեկ տող է
        var liked = (await _db.Feedbacks.AsNoTracking()
                .Where(f => f.UserId == userId && f.Reaction == FeedbackReaction.Liked)
                .OrderByDescending(f => f.CreatedAtUtc)
                .Select(f => new RecipeRefDto { Title = f.Recipe.Title, Slug = f.Recipe.Slug, AtUtc = f.CreatedAtUtc })
                .ToListAsync())
            .DistinctBy(r => r.Slug)
            .ToList();

        var recent = await activity
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(RecentListSize)
            .Select(a => new { a.Type, a.Target, a.Path, a.RecipeId, a.CreatedAtUtc })
            .ToListAsync();

        var recipeIds = recent.Where(r => r.RecipeId != null).Select(r => r.RecipeId!.Value).Distinct().ToList();
        var recipes = await _db.Recipes.AsNoTracking()
            .Where(r => recipeIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Title, r.Slug })
            .ToDictionaryAsync(r => r.Id);

        return new AdminUserActivityDto
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            CreatedAtUtc = AsUtc(user.CreatedAtUtc),
            LastActiveUtc = lastActive is { } last ? AsUtc(last) : null,
            Days = days,
            PageViews = byType.GetValueOrDefault(ActivityType.PageView),
            Clicks = byType.GetValueOrDefault(ActivityType.Click),
            RecipeViews = byType.GetValueOrDefault(ActivityType.RecipeView),
            SavedRecipes = saved.Select(r => { r.AtUtc = AsUtc(r.AtUtc); return r; }).ToList(),
            LikedRecipes = liked.Select(r => { r.AtUtc = AsUtc(r.AtUtc); return r; }).ToList(),
            MostViewedRecipes = await TopRecipesAsync(RecipeViewIds(activity)),
            TopClicks = await TopLabelsAsync(activity.Where(a => a.Type == ActivityType.Click).Select(a => a.Target!)),
            TopPages = await TopLabelsAsync(activity.Where(a => a.Type == ActivityType.PageView && a.Path != null).Select(a => a.Path!)),
            Recent = recent.Select(r =>
            {
                var recipe = r.RecipeId is { } rid && recipes.TryGetValue(rid, out var found) ? found : null;
                return new ActivityLogItemDto
                {
                    Type = r.Type,
                    Target = r.Target,
                    Path = r.Path,
                    RecipeTitle = recipe?.Title,
                    RecipeSlug = recipe?.Slug,
                    AtUtc = AsUtc(r.CreatedAtUtc)
                };
            }).ToList()
        };
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<Dictionary<ActivityType, int>> CountByTypeAsync(IQueryable<UserActivity> activity) =>
        await activity
            .GroupBy(a => a.Type)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

    private static IQueryable<Guid> RecipeViewIds(IQueryable<UserActivity> activity) =>
        activity.Where(a => a.Type == ActivityType.RecipeView && a.RecipeId != null).Select(a => a.RecipeId!.Value);

    /// <summary>Ամենահաճախ հանդիպող ռեցեպտները՝ անուններով։ Ջնջված ռեցեպտները դուրս են մնում։</summary>
    private async Task<List<StatCountDto>> TopRecipesAsync(IQueryable<Guid> recipeIds)
    {
        var top = await recipeIds
            .GroupBy(id => id)
            .Select(g => new { RecipeId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(TopListSize)
            .ToListAsync();

        var ids = top.Select(t => t.RecipeId).ToList();
        var recipes = await _db.Recipes.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.Title, r.Slug })
            .ToDictionaryAsync(r => r.Id);

        return top
            .Where(t => recipes.ContainsKey(t.RecipeId))
            .Select(t => new StatCountDto { Label = recipes[t.RecipeId].Title, Slug = recipes[t.RecipeId].Slug, Count = t.Count })
            .ToList();
    }

    private static async Task<List<StatCountDto>> TopLabelsAsync(IQueryable<string> labels) =>
        await labels
            .GroupBy(l => l)
            .Select(g => new StatCountDto { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(TopListSize)
            .ToListAsync();

    public const int MaxUserRows = 200;

    private async Task<List<AdminUserStatsRowDto>> UserRowsAsync(IQueryable<UserActivity> periodActivity)
    {
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.User)
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync();

        var events = await periodActivity
            .Where(a => a.UserId != null)
            .GroupBy(a => a.UserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        var lastActive = await _db.UserActivities.AsNoTracking()
            .Where(a => a.UserId != null)
            .GroupBy(a => a.UserId!.Value)
            .Select(g => new { UserId = g.Key, Last = g.Max(a => a.CreatedAtUtc) })
            .ToDictionaryAsync(x => x.UserId, x => x.Last);

        var favorites = await _db.FavoriteRecipes.AsNoTracking()
            .GroupBy(f => f.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        return users
            .Select(u => new AdminUserStatsRowDto
            {
                Id = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email,
                Events = events.GetValueOrDefault(u.Id),
                Favorites = favorites.GetValueOrDefault(u.Id),
                LastActiveUtc = lastActive.TryGetValue(u.Id, out var last) ? AsUtc(last) : null
            })
            .OrderByDescending(r => r.Events)
            .ThenByDescending(r => r.LastActiveUtc)
            .ToList();
    }

    // SQL-ից Kind-ը Unspecified է գալիս
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? NormalizeTarget(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var collapsed = Whitespace().Replace(value.Trim(), " ");
        return collapsed.Length <= 120 ? collapsed : collapsed[..120];
    }

    /// <summary>
    /// "/admin/blog/3f2c…?x=1" → "/admin/blog/{id}"։ Query string-ը կարող է token կամ
    /// returnUrl պարունակել, իսկ GUID-ները ամեն պլան/ցուցակ առանձին տող կդարձնեին։
    /// </summary>
    public static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var path = value.Trim();
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
            path = path[..cut];

        path = GuidSegment().Replace(path, "{id}");

        if (!path.StartsWith('/'))
            path = "/" + path;
        if (path.Length > 1)
            path = path.TrimEnd('/');

        return path.Length <= 300 ? path : path[..300];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidSegment();
}
