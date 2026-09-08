// Gialora.Application/Services/FeedbackService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;

namespace Gialora.Application.Services;

/// <summary>
/// "After meals, user can optionally give feedback" (app structure §1)։
/// Այս տվյալը կարդում է <see cref="MealPlanService"/>-ը հաջորդ պլանը կառուցելիս։
/// </summary>
public class FeedbackService : IFeedbackService
{
    private readonly GialoraDbContext _db;
    private readonly ILogger<FeedbackService> _logger;

    public FeedbackService(GialoraDbContext db, ILogger<FeedbackService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<FeedbackDto> UpsertAsync(Guid userId, Guid recipeId, FeedbackUpsertDto dto)
    {
        var recipe = await _db.Recipes.AsNoTracking()
            .Where(r => r.Id == recipeId)
            .Select(r => new { r.Id, r.Title })
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("Recipe not found.");

        var feedback = await _db.Feedbacks
            .FirstOrDefaultAsync(f => f.RecipeId == recipeId && f.UserId == userId);

        if (feedback is null)
        {
            feedback = new Feedback { RecipeId = recipeId, UserId = userId };
            _db.Feedbacks.Add(feedback);
        }

        feedback.Rating = Math.Clamp(dto.Rating, 0, 5);
        feedback.Reaction = dto.Reaction;
        feedback.TooDifficult = dto.TooDifficult;
        feedback.KidsDidNotEat = dto.KidsDidNotEat;
        feedback.TookTooLong = dto.TookTooLong;
        feedback.Comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
        feedback.IsDeleted = false; // նախկինում ջնջածը վերականգնում ենք, չենք կրկնօրինակում

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Երկու զուգահեռ request նույն (recipe, user) զույգով — unique index-ը բռնեց։
            // Առանց սրա սա 500 կլիներ; փոխարենը կարդում ենք հաղթող տողը։
            _db.Entry(feedback).State = EntityState.Detached;

            var winner = await _db.Feedbacks
                .FirstOrDefaultAsync(f => f.RecipeId == recipeId && f.UserId == userId);

            if (winner is null)
                throw; // ուրիշ DB սխալ էր, ոչ թե unique-index-ի բախում

            feedback = winner;
        }

        _logger.LogInformation("Feedback saved for recipe {RecipeId} by user {UserId}", recipeId, userId);
        return Map(feedback, recipe.Title);
    }

    public async Task<RecipeRatingSummaryDto> GetSummaryAsync(Guid recipeId, Guid? currentUserId)
    {
        var stats = await _db.Feedbacks.AsNoTracking()
            .Where(f => f.RecipeId == recipeId)
            .GroupBy(f => f.RecipeId)
            .Select(g => new
            {
                Average = g.Where(f => f.Rating > 0).Average(f => (double?)f.Rating) ?? 0d,
                Count = g.Count(f => f.Rating > 0),
                Liked = g.Count(f => f.Reaction == FeedbackReaction.Liked),
                Disliked = g.Count(f => f.Reaction == FeedbackReaction.Disliked)
            })
            .FirstOrDefaultAsync();

        var summary = new RecipeRatingSummaryDto
        {
            RecipeId = recipeId,
            AverageRating = Math.Round(stats?.Average ?? 0d, 2),
            RatingCount = stats?.Count ?? 0,
            LikedCount = stats?.Liked ?? 0,
            DislikedCount = stats?.Disliked ?? 0
        };

        if (currentUserId is { } userId)
        {
            summary.MyFeedback = await _db.Feedbacks.AsNoTracking()
                .Where(f => f.RecipeId == recipeId && f.UserId == userId)
                .Select(f => new FeedbackDto
                {
                    Id = f.Id,
                    RecipeId = f.RecipeId,
                    RecipeTitle = f.Recipe.Title,
                    Rating = f.Rating,
                    Reaction = f.Reaction,
                    TooDifficult = f.TooDifficult,
                    KidsDidNotEat = f.KidsDidNotEat,
                    TookTooLong = f.TookTooLong,
                    Comment = f.Comment,
                    CreatedAtUtc = f.CreatedAtUtc
                })
                .FirstOrDefaultAsync();
        }

        return summary;
    }

    public Task<List<FeedbackDto>> GetMyFeedbackAsync(Guid userId) =>
        _db.Feedbacks.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new FeedbackDto
            {
                Id = f.Id,
                RecipeId = f.RecipeId,
                RecipeTitle = f.Recipe.Title,
                Rating = f.Rating,
                Reaction = f.Reaction,
                TooDifficult = f.TooDifficult,
                KidsDidNotEat = f.KidsDidNotEat,
                TookTooLong = f.TookTooLong,
                Comment = f.Comment,
                CreatedAtUtc = f.CreatedAtUtc
            })
            .ToListAsync();

    public async Task<bool> DeleteAsync(Guid userId, Guid recipeId)
    {
        var feedback = await _db.Feedbacks
            .FirstOrDefaultAsync(f => f.RecipeId == recipeId && f.UserId == userId);

        if (feedback is null)
            return false;

        feedback.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    private static FeedbackDto Map(Feedback f, string recipeTitle) => new()
    {
        Id = f.Id,
        RecipeId = f.RecipeId,
        RecipeTitle = recipeTitle,
        Rating = f.Rating,
        Reaction = f.Reaction,
        TooDifficult = f.TooDifficult,
        KidsDidNotEat = f.KidsDidNotEat,
        TookTooLong = f.TookTooLong,
        Comment = f.Comment,
        CreatedAtUtc = f.CreatedAtUtc
    };
}

/// <summary>"Save/favorite recipes" — MVP-ի վերջին կետը (app structure §9)։</summary>
public class FavoriteService : IFavoriteService
{
    private readonly GialoraDbContext _db;

    public FavoriteService(GialoraDbContext db)
    {
        _db = db;
    }

    public async Task<bool> AddAsync(Guid userId, Guid recipeId)
    {
        if (!await _db.Recipes.AnyAsync(r => r.Id == recipeId && r.IsPublished))
            throw new NotFoundException("Recipe not found.");

        if (await _db.FavoriteRecipes.AnyAsync(f => f.UserId == userId && f.RecipeId == recipeId))
            return false; // արդեն ընտրանիում է — idempotent է, սխալ չենք նետում

        _db.FavoriteRecipes.Add(new FavoriteRecipe { UserId = userId, RecipeId = recipeId });
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid recipeId)
    {
        var favorite = await _db.FavoriteRecipes
            .FirstOrDefaultAsync(f => f.UserId == userId && f.RecipeId == recipeId);

        if (favorite is null)
            return false;

        _db.FavoriteRecipes.Remove(favorite); // hard delete — join table-ը audit չի պահանջում
        await _db.SaveChangesAsync();
        return true;
    }

    public Task<List<RecipeSummaryDto>> GetMyFavoritesAsync(Guid userId) =>
        _db.FavoriteRecipes.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => f.Recipe)
            .Select(RecipeMappings.SummaryProjection(userId))
            .ToListAsync();
}
