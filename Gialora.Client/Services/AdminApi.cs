// Gialora.Client/Services/AdminApi.cs
using Gialora.Client.Localization;
using Gialora.Shared.Dtos;

namespace Gialora.Client.Services;

/// <summary>Admin panel-ի ամբողջ մակերեսը (app structure §11, §12)։</summary>
public class AdminApi : ApiClientBase
{
    public AdminApi(HttpClient http, Localizer localizer) : base(http, localizer) { }

    // --- Recipes ---

    /// <summary>published: null = բոլորը, false = միայն draft-երը։</summary>
    public Task<PagedResult<RecipeSummaryDto>> SearchRecipesAsync(RecipeFilterDto filter, bool? published)
    {
        var query = filter.ToQueryString();
        var separator = string.IsNullOrEmpty(query) ? "?" : "&";
        var publishedPart = published is null ? string.Empty : $"{separator}published={published.Value.ToString().ToLowerInvariant()}";

        return GetAsync<PagedResult<RecipeSummaryDto>>($"api/recipes/admin{query}{publishedPart}");
    }

    public Task<RecipeDetailDto?> GetRecipeAsync(Guid id) =>
        GetOrDefaultAsync<RecipeDetailDto>($"api/recipes/admin/{id}");

    public Task<Guid> CreateRecipeAsync(RecipeCreateDto dto) =>
        PostAsync<Guid>("api/recipes", dto);

    public Task UpdateRecipeAsync(Guid id, RecipeUpdateDto dto) =>
        PutAsync($"api/recipes/{id}", dto);

    public Task PublishRecipeAsync(Guid id) => PostAsync($"api/recipes/{id}/publish");

    public Task UnpublishRecipeAsync(Guid id) => PostAsync($"api/recipes/{id}/unpublish");

    public Task DeleteRecipeAsync(Guid id) => DeleteAsync($"api/recipes/{id}");

    // Թարգմանությունները — admin-ի լեզուների tab-երը (մեկ PUT/DELETE ամեն լեզվի համար)

    public Task<List<RecipeTextDto>> GetRecipeTranslationsAsync(Guid id) =>
        GetAsync<List<RecipeTextDto>>($"api/recipes/admin/{id}/translations");

    public Task SaveRecipeTranslationAsync(Guid id, RecipeTextDto dto) =>
        PutAsync($"api/recipes/{id}/translations/{dto.Language}", dto);

    public Task DeleteRecipeTranslationAsync(Guid id, string language) =>
        DeleteAsync($"api/recipes/{id}/translations/{language}");

    // --- Ingredients ---

    public Task<List<IngredientNameDto>> GetIngredientTranslationsAsync(Guid id) =>
        GetAsync<List<IngredientNameDto>>($"api/ingredients/{id}/translations");

    public Task SaveIngredientTranslationAsync(Guid id, IngredientNameDto dto) =>
        PutAsync($"api/ingredients/{id}/translations/{dto.Language}", dto);

    public Task DeleteIngredientTranslationAsync(Guid id, string language) =>
        DeleteAsync($"api/ingredients/{id}/translations/{language}");

    // --- Tags ---

    public Task<List<TagAdminDto>> GetTagsAsync() =>
        GetAsync<List<TagAdminDto>>("api/tags/admin");

    public Task SaveTagTranslationAsync(Guid id, TagNameDto dto) =>
        PutAsync($"api/tags/{id}/translations/{dto.Language}", dto);

    public Task<IngredientDto> CreateIngredientAsync(IngredientCreateDto dto) =>
        PostAsync<IngredientDto>("api/ingredients", dto);

    public Task<IngredientDto> UpdateIngredientAsync(Guid id, IngredientUpdateDto dto) =>
        PutAsync<IngredientDto>($"api/ingredients/{id}", dto);

    public Task DeleteIngredientAsync(Guid id) => DeleteAsync($"api/ingredients/{id}");

    // --- Bulk import (app structure §12) ---

    public Task<RecipeImportResultDto> ImportRecipesAsync(RecipeImportRequestDto request) =>
        PostAsync<RecipeImportResultDto>("api/admin/import/recipes", request);

    /// <summary>Template-ը վերադարձնում ենք որպես տեքստ — client-ը այն պահում է որպես ֆայլ։</summary>
    public async Task<string> GetImportTemplateAsync()
    {
        var response = await SendSafeAsync(() => Http.GetAsync("api/admin/import/template"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    // --- Blog ---

    public Task<List<BlogPostSummaryDto>> GetPostsAsync() =>
        GetAsync<List<BlogPostSummaryDto>>("api/blog/admin");

    public Task<BlogPostDetailDto?> GetPostAsync(Guid id) =>
        GetOrDefaultAsync<BlogPostDetailDto>($"api/blog/admin/{id}");

    public Task<Guid> CreatePostAsync(BlogPostCreateDto dto) =>
        PostAsync<Guid>("api/blog", dto);

    public Task UpdatePostAsync(Guid id, BlogPostUpdateDto dto) =>
        PutAsync($"api/blog/{id}", dto);

    public Task DeletePostAsync(Guid id) => DeleteAsync($"api/blog/{id}");

    public Task<List<BlogPostTextDto>> GetPostTranslationsAsync(Guid id) =>
        GetAsync<List<BlogPostTextDto>>($"api/blog/admin/{id}/translations");

    public Task SavePostTranslationAsync(Guid id, BlogPostTextDto dto) =>
        PutAsync($"api/blog/{id}/translations/{dto.Language}", dto);

    public Task DeletePostTranslationAsync(Guid id, string language) =>
        DeleteAsync($"api/blog/{id}/translations/{language}");

    // --- Users ---

    public Task<List<AdminUserDto>> GetUsersAsync() =>
        GetAsync<List<AdminUserDto>>("api/admin/users");

    public Task<AdminUserDto> CreateUserAsync(AdminUserCreateDto dto) =>
        PostAsync<AdminUserDto>("api/admin/users", dto);

    public Task SetUserPasswordAsync(Guid id, string newPassword) =>
        PutAsync($"api/admin/users/{id}/password", new AdminSetPasswordDto { NewPassword = newPassword });

    public Task DeleteUserAsync(Guid id) => DeleteAsync($"api/admin/users/{id}");

    // --- Statistics ---

    public Task<AdminStatsDto> GetStatsAsync(int days) =>
        GetAsync<AdminStatsDto>($"api/admin/stats?days={days}");

    public Task<AdminUserActivityDto?> GetUserActivityAsync(Guid id, int days) =>
        GetOrDefaultAsync<AdminUserActivityDto>($"api/admin/stats/users/{id}?days={days}");
}
