// Gialora.Tests/ActivityServiceTests.cs
using Gialora.Application.Common;
using Gialora.Application.Services;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Gialora.Tests;

/// <summary>
/// Admin statistics: the browser sends batches of page views / clicks / recipe views,
/// and the admin sees what users like (saved, 👍), click and do. Admins' own activity
/// must never pollute the numbers, and paths must not leak query strings or per-plan ids.
/// </summary>
public class ActivityServiceTests
{
    private readonly GialoraDbContext _db = new(new DbContextOptionsBuilder<GialoraDbContext>()
        .UseInMemoryDatabase($"activity-{Guid.NewGuid()}")
        .Options);

    private ActivityService Service() => new(_db);

    private User AddUser(string name, UserRole role = UserRole.User)
    {
        var user = new User { Email = $"{name}@test.local", DisplayName = name, Role = role };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    private Recipe AddRecipe(string title, Guid adminId)
    {
        var recipe = new Recipe { Title = title, Slug = title.ToLowerInvariant().Replace(' ', '-'), CreatedByAdminId = adminId, IsPublished = true };
        _db.Recipes.Add(recipe);
        _db.SaveChanges();
        return recipe;
    }

    private static ActivityBatchDto Batch(params ActivityEventDto[] events) => new() { Events = events.ToList() };

    private static ActivityEventDto Click(string target, string path = "/recipes") =>
        new() { Type = ActivityType.Click, Target = target, Path = path };

    [Fact]
    public async Task A_users_batch_is_stored_against_them()
    {
        var user = AddUser("maria");

        await Service().RecordAsync(user.Id, Batch(
            new ActivityEventDto { Type = ActivityType.PageView, Path = "/weekly" },
            Click("Generate plan", "/weekly")));

        var rows = await _db.UserActivities.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(user.Id, r.UserId));
        Assert.Contains(rows, r => r.Type == ActivityType.Click && r.Target == "Generate plan");
    }

    [Fact]
    public async Task Admin_activity_is_not_recorded()
    {
        var admin = AddUser("boss", UserRole.Admin);

        await Service().RecordAsync(admin.Id, Batch(Click("Publish")));

        Assert.Empty(await _db.UserActivities.ToListAsync());
    }

    [Fact]
    public async Task Guests_are_recorded_without_a_user()
    {
        await Service().RecordAsync(null, Batch(Click("Browse recipes", "/")));

        var row = await _db.UserActivities.SingleAsync();
        Assert.Null(row.UserId);
    }

    [Fact]
    public async Task Malformed_events_are_dropped()
    {
        await Service().RecordAsync(null, Batch(
            new ActivityEventDto { Type = ActivityType.Click, Target = "   " },        // click with no label
            new ActivityEventDto { Type = ActivityType.RecipeView, RecipeId = null },  // recipe view with no recipe
            new ActivityEventDto { Type = (ActivityType)99, Path = "/" }));            // unknown type

        Assert.Empty(await _db.UserActivities.ToListAsync());
    }

    [Theory]
    [InlineData("/plans/3f2c8a1e-1b2c-4d5e-8f90-123456789abc?tab=shopping", "/plans/{id}")]
    [InlineData("/login?returnUrl=%2Fsecret", "/login")]
    [InlineData("recipes/greek-salad/", "/recipes/greek-salad")]
    [InlineData("/", "/")]
    public void Paths_lose_query_strings_and_ids(string raw, string expected) =>
        Assert.Equal(expected, ActivityService.NormalizePath(raw));

    [Fact]
    public async Task Stats_rank_clicks_and_recipes()
    {
        var admin = AddUser("boss", UserRole.Admin);
        var maria = AddUser("maria");
        var nikos = AddUser("nikos");
        var salad = AddRecipe("Greek salad", admin.Id);
        var soup = AddRecipe("Lentil soup", admin.Id);

        _db.FavoriteRecipes.AddRange(
            new FavoriteRecipe { UserId = maria.Id, RecipeId = salad.Id },
            new FavoriteRecipe { UserId = nikos.Id, RecipeId = salad.Id },
            new FavoriteRecipe { UserId = nikos.Id, RecipeId = soup.Id });

        // Two 👍 from the same person on the same recipe count once
        _db.Feedbacks.AddRange(
            new Feedback { UserId = maria.Id, RecipeId = soup.Id, Reaction = FeedbackReaction.Liked },
            new Feedback { UserId = maria.Id, RecipeId = soup.Id, Reaction = FeedbackReaction.Liked },
            new Feedback { UserId = nikos.Id, RecipeId = salad.Id, Reaction = FeedbackReaction.Disliked });
        await _db.SaveChangesAsync();

        await Service().RecordAsync(maria.Id, Batch(Click("Save"), Click("Save"), Click("Print"),
            new ActivityEventDto { Type = ActivityType.RecipeView, RecipeId = soup.Id, Path = "/recipes/lentil-soup" }));
        await Service().RecordAsync(nikos.Id, Batch(Click("Save")));

        var stats = await Service().GetStatsAsync(30);

        Assert.Equal(2, stats.TotalUsers);   // admins aren't counted as users
        Assert.Equal(2, stats.ActiveUsers);
        Assert.Equal(4, stats.Clicks);
        Assert.Equal(1, stats.RecipeViews);

        Assert.Equal("Save", stats.TopClicks[0].Label);
        Assert.Equal(3, stats.TopClicks[0].Count);

        Assert.Equal("Greek salad", stats.MostSavedRecipes[0].Label);
        Assert.Equal(2, stats.MostSavedRecipes[0].Count);

        var liked = Assert.Single(stats.MostLikedRecipes);
        Assert.Equal("Lentil soup", liked.Label);
        Assert.Equal(1, liked.Count);

        Assert.Equal("lentil-soup", Assert.Single(stats.MostViewedRecipes).Slug);

        Assert.Equal(maria.Id, stats.Users[0].Id);   // most active first
        Assert.Equal(4, stats.Users[0].Events);
    }

    [Fact]
    public async Task User_detail_shows_what_they_saved_liked_and_did()
    {
        var admin = AddUser("boss", UserRole.Admin);
        var maria = AddUser("maria");
        var soup = AddRecipe("Lentil soup", admin.Id);

        _db.FavoriteRecipes.Add(new FavoriteRecipe { UserId = maria.Id, RecipeId = soup.Id });
        _db.Feedbacks.Add(new Feedback { UserId = maria.Id, RecipeId = soup.Id, Reaction = FeedbackReaction.Liked });
        await _db.SaveChangesAsync();

        await Service().RecordAsync(maria.Id, Batch(
            new ActivityEventDto { Type = ActivityType.RecipeView, RecipeId = soup.Id, Path = "/recipes/lentil-soup" },
            Click("Save", "/recipes/lentil-soup")));

        var detail = await Service().GetUserActivityAsync(maria.Id, 30);

        Assert.Equal("Lentil soup", Assert.Single(detail.SavedRecipes).Title);
        Assert.Equal("Lentil soup", Assert.Single(detail.LikedRecipes).Title);
        Assert.Equal(1, detail.Clicks);
        Assert.Equal(2, detail.Recent.Count);
        Assert.Contains(detail.Recent, r => r.Type == ActivityType.RecipeView && r.RecipeTitle == "Lentil soup");
        Assert.NotNull(detail.LastActiveUtc);
    }

    [Fact]
    public async Task Unknown_user_detail_is_not_found() =>
        await Assert.ThrowsAsync<NotFoundException>(() => Service().GetUserActivityAsync(Guid.NewGuid(), 30));
}
