// Gialora.Application/Services/IShoppingListService.cs
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IShoppingListService
{
    /// <summary>
    /// Meal plan-ից գեներացնում է consolidate արված գնումների ցանկ (app structure §1, §3)։
    /// Կրկնակի կանչը վերագեներացնում է ցանկը՝ պահպանելով արդեն նշված տողերը։
    /// </summary>
    Task<ShoppingListDto> GenerateFromMealPlanAsync(Guid userId, Guid mealPlanId);

    Task<List<ShoppingListDto>> GetMyListsAsync(Guid userId);
    Task<ShoppingListDto?> GetByIdAsync(Guid userId, Guid listId);
    Task<ShoppingListDto?> GetForMealPlanAsync(Guid userId, Guid mealPlanId);

    Task<ShoppingListItemDto?> SetItemCheckedAsync(Guid userId, Guid listId, Guid itemId, bool isChecked);
    Task<ShoppingListItemDto> AddItemAsync(Guid userId, Guid listId, ShoppingListItemCreateDto dto);
    Task<bool> RemoveItemAsync(Guid userId, Guid listId, Guid itemId);

    /// <summary>Գնումներից հետո՝ մեկ շարժումով մաքրում ենք նշվածները։</summary>
    Task<ShoppingListDto?> ClearCheckedAsync(Guid userId, Guid listId);

    Task<bool> DeleteAsync(Guid userId, Guid listId);
}
