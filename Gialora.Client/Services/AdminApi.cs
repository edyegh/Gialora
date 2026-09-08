// Gialora.Client/Services/AdminApi.cs
using Gialora.Shared.Dtos;

namespace Gialora.Client.Services;

/// <summary>Admin panel-ի ամբողջ մակերեսը (app structure §11, §12)։</summary>
public class AdminApi : ApiClientBase
{
    public AdminApi(HttpClient http) : base(http) { }

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

    // --- Ingredients ---

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
        var response = await Http.GetAsync("api/admin/import/template");
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
}
