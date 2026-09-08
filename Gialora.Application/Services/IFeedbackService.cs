// Gialora.Application/Services/IFeedbackService.cs
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IFeedbackService
{
    /// <summary>Upsert — մեկ user + մեկ recipe = մեկ feedback (unique index-ը դա է պահանջում)։</summary>
    Task<FeedbackDto> UpsertAsync(Guid userId, Guid recipeId, FeedbackUpsertDto dto);

    Task<RecipeRatingSummaryDto> GetSummaryAsync(Guid recipeId, Guid? currentUserId);
    Task<List<FeedbackDto>> GetMyFeedbackAsync(Guid userId);
    Task<bool> DeleteAsync(Guid userId, Guid recipeId);
}

public interface IFavoriteService
{
    Task<bool> AddAsync(Guid userId, Guid recipeId);
    Task<bool> RemoveAsync(Guid userId, Guid recipeId);
    Task<List<RecipeSummaryDto>> GetMyFavoritesAsync(Guid userId);
}
