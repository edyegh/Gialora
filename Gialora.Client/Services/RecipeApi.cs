// Gialora.Client/Services/RecipeApi.cs
using Gialora.Shared.Dtos;

namespace Gialora.Client.Services;

/// <summary>Product 1 — recipe discovery (app structure §6)։</summary>
public class RecipeApi : ApiClientBase
{
    public RecipeApi(HttpClient http) : base(http) { }

    public Task<PagedResult<RecipeSummaryDto>> SearchAsync(RecipeFilterDto filter) =>
        GetAsync<PagedResult<RecipeSummaryDto>>($"api/recipes{filter.ToQueryString()}");

    public Task<RecipeDetailDto?> GetByIdAsync(Guid id) =>
        GetOrDefaultAsync<RecipeDetailDto>($"api/recipes/{id}");

    public Task<RecipeDetailDto?> GetBySlugAsync(string slug) =>
        GetOrDefaultAsync<RecipeDetailDto>($"api/recipes/by-slug/{Uri.EscapeDataString(slug)}");

    public Task<RecipeRatingSummaryDto> GetRatingsAsync(Guid id) =>
        GetAsync<RecipeRatingSummaryDto>($"api/recipes/{id}/ratings");

    public Task<List<TagDto>> GetTagsAsync() =>
        GetAsync<List<TagDto>>("api/tags");

    public Task<List<IngredientDto>> GetIngredientsAsync(string? search = null) =>
        GetAsync<List<IngredientDto>>(
            string.IsNullOrWhiteSpace(search)
                ? "api/ingredients"
                : $"api/ingredients?search={Uri.EscapeDataString(search)}");

    // --- Favourites (app structure §9) ---

    public Task<List<RecipeSummaryDto>> GetFavoritesAsync() =>
        GetAsync<List<RecipeSummaryDto>>("api/favorites");

    public Task AddFavoriteAsync(Guid recipeId) => PutAsync($"api/favorites/{recipeId}");

    public Task RemoveFavoriteAsync(Guid recipeId) => DeleteAsync($"api/favorites/{recipeId}");

    // --- Feedback (app structure §1) ---

    public Task<FeedbackDto> SaveFeedbackAsync(Guid recipeId, FeedbackUpsertDto dto) =>
        PutAsync<FeedbackDto>($"api/feedback/recipes/{recipeId}", dto);

    public Task<List<FeedbackDto>> GetMyFeedbackAsync() =>
        GetAsync<List<FeedbackDto>>("api/feedback/mine");
}
