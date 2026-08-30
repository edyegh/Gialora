using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IRecipeService
{
    Task<IEnumerable<RecipeSummaryDto>> GetPublishedRecipesAsync(int page, int pageSize);
    Task<RecipeDetailDto?> GetRecipeByIdAsync(Guid id);
    Task<Guid> CreateRecipeAsync(RecipeCreateDto dto, Guid createdByAdminId);
    Task<bool> DeleteRecipeAsync(Guid id);
}