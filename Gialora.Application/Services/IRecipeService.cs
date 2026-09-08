// Gialora.Application/Services/IRecipeService.cs
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IRecipeService
{
    /// <summary>Public catalog — միայն published ռեցեպտները, ամբողջական filtering-ով։</summary>
    Task<PagedResult<RecipeSummaryDto>> SearchAsync(RecipeFilterDto filter, Guid? currentUserId);

    /// <summary>Admin catalog — ներառում է draft-երը (app structure §11)։</summary>
    Task<PagedResult<RecipeSummaryDto>> SearchForAdminAsync(RecipeFilterDto filter, bool? publishedOnly);

    Task<RecipeDetailDto?> GetByIdAsync(Guid id, Guid? currentUserId, bool includeUnpublished = false);
    Task<RecipeDetailDto?> GetBySlugAsync(string slug, Guid? currentUserId, bool includeUnpublished = false);

    Task<Guid> CreateAsync(RecipeCreateDto dto, Guid createdByAdminId);
    Task<bool> UpdateAsync(Guid id, RecipeUpdateDto dto, Guid editedByAdminId);
    Task<bool> SetPublishedAsync(Guid id, bool isPublished);
    Task<bool> DeleteAsync(Guid id);
}
