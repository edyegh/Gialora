// Gialora.Client/Services/PlannerApi.cs
using Gialora.Shared.Dtos;

namespace Gialora.Client.Services;

/// <summary>
/// Product 2 — personal meal planner (app structure §6)։ Ընտանիք → պլան → գնումների ցանկ։
/// </summary>
public class PlannerApi : ApiClientBase
{
    public PlannerApi(HttpClient http) : base(http) { }

    // --- Family ---

    public Task<FamilyDto> GetFamilyAsync() => GetAsync<FamilyDto>("api/family/me");

    public Task<FamilyDto> RenameFamilyAsync(string name) =>
        PutAsync<FamilyDto>("api/family/me", new FamilyUpdateDto { Name = name });

    public Task<FamilyPreferencesDto> SavePreferencesAsync(FamilyPreferencesDto dto) =>
        PutAsync<FamilyPreferencesDto>("api/family/me/preferences", dto);

    public Task<FamilyMemberDto> AddMemberAsync(FamilyMemberCreateDto dto) =>
        PostAsync<FamilyMemberDto>("api/family/members", dto);

    public Task<FamilyMemberDto> UpdateMemberAsync(Guid memberId, FamilyMemberUpdateDto dto) =>
        PutAsync<FamilyMemberDto>($"api/family/members/{memberId}", dto);

    public Task RemoveMemberAsync(Guid memberId) => DeleteAsync($"api/family/members/{memberId}");

    // --- Meal plans ---

    public Task<List<MealPlanDto>> GetPlansAsync() => GetAsync<List<MealPlanDto>>("api/mealplans");

    /// <summary>Ընթացիկ պլանը կարող է դեռ գոյություն չունենալ — 204/404-ը null է։</summary>
    public Task<MealPlanDto?> GetCurrentPlanAsync() => GetOrDefaultAsync<MealPlanDto>("api/mealplans/current");

    public Task<MealPlanDto?> GetPlanAsync(Guid planId) => GetOrDefaultAsync<MealPlanDto>($"api/mealplans/{planId}");

    public Task<MealPlanDto> GenerateAsync(MealPlanGenerateDto dto) =>
        PostAsync<MealPlanDto>("api/mealplans/generate", dto);

    public Task<MealPlanDto> SwapEntryAsync(Guid planId, Guid entryId, Guid? recipeId = null) =>
        PostAsync<MealPlanDto>($"api/mealplans/{planId}/entries/{entryId}/swap",
            new MealPlanSwapDto { RecipeId = recipeId });

    public Task<MealPlanDto> SetEntryLockedAsync(Guid planId, Guid entryId, bool locked) =>
        PostAsync<MealPlanDto>($"api/mealplans/{planId}/entries/{entryId}/lock?locked={locked.ToString().ToLowerInvariant()}");

    public Task<MealPlanDto> RegenerateDayAsync(Guid planId, Guid dayId) =>
        PostAsync<MealPlanDto>($"api/mealplans/{planId}/days/{dayId}/regenerate");

    public Task<MealPlanDto> RegenerateAsync(Guid planId) =>
        PostAsync<MealPlanDto>($"api/mealplans/{planId}/regenerate");

    public Task<MealPlanDto> AcceptAsync(Guid planId) =>
        PostAsync<MealPlanDto>($"api/mealplans/{planId}/accept");

    public Task DeletePlanAsync(Guid planId) => DeleteAsync($"api/mealplans/{planId}");

    // --- Shopping lists ---

    public Task<ShoppingListDto> BuildShoppingListAsync(Guid planId) =>
        PostAsync<ShoppingListDto>($"api/mealplans/{planId}/shopping-list");

    public Task<ShoppingListDto?> GetShoppingListForPlanAsync(Guid planId) =>
        GetOrDefaultAsync<ShoppingListDto>($"api/mealplans/{planId}/shopping-list");

    public Task<List<ShoppingListDto>> GetShoppingListsAsync() =>
        GetAsync<List<ShoppingListDto>>("api/shopping-lists");

    public Task<ShoppingListDto?> GetShoppingListAsync(Guid listId) =>
        GetOrDefaultAsync<ShoppingListDto>($"api/shopping-lists/{listId}");

    public Task<ShoppingListItemDto> SetItemCheckedAsync(Guid listId, Guid itemId, bool isChecked) =>
        PutAsync<ShoppingListItemDto>($"api/shopping-lists/{listId}/items/{itemId}/checked",
            new ShoppingListItemCheckDto { IsChecked = isChecked });

    public Task<ShoppingListItemDto> AddShoppingItemAsync(Guid listId, ShoppingListItemCreateDto dto) =>
        PostAsync<ShoppingListItemDto>($"api/shopping-lists/{listId}/items", dto);

    public Task RemoveShoppingItemAsync(Guid listId, Guid itemId) =>
        DeleteAsync($"api/shopping-lists/{listId}/items/{itemId}");

    public Task<ShoppingListDto> ClearCheckedAsync(Guid listId) =>
        PostAsync<ShoppingListDto>($"api/shopping-lists/{listId}/clear-checked");

    public Task DeleteShoppingListAsync(Guid listId) => DeleteAsync($"api/shopping-lists/{listId}");
}
