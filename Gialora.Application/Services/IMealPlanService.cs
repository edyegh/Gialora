// Gialora.Application/Services/IMealPlanService.cs
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IMealPlanService
{
    /// <summary>"App generates a weekly/biweekly/batch meal plan" (app structure §1)։</summary>
    Task<MealPlanDto> GenerateAsync(Guid userId, MealPlanGenerateDto dto);

    Task<List<MealPlanDto>> GetMyPlansAsync(Guid userId);
    Task<MealPlanDto?> GetByIdAsync(Guid userId, Guid planId);

    /// <summary>Ամենավերջին ակտիվ պլանը — Home էջի "your week" widget-ի համար։</summary>
    Task<MealPlanDto?> GetCurrentAsync(Guid userId);

    /// <summary>"Replace one meal"։ recipeId = null → engine-ը ինքն է ընտրում։</summary>
    Task<MealPlanDto?> SwapEntryAsync(Guid userId, Guid planId, Guid entryId, MealPlanSwapDto dto);

    /// <summary>"Replace an entire day"։</summary>
    Task<MealPlanDto?> RegenerateDayAsync(Guid userId, Guid planId, Guid dayId);

    /// <summary>"Regenerate the week" — locked ճաշերը մնում են տեղում։</summary>
    Task<MealPlanDto?> RegenerateAsync(Guid userId, Guid planId);

    /// <summary>Ճաշը "կողպելը" պաշտպանում է regenerate-ից։</summary>
    Task<MealPlanDto?> SetEntryLockedAsync(Guid userId, Guid planId, Guid entryId, bool isLocked);

    /// <summary>"Accept it" — պլանը դառնում է վերջնական։</summary>
    Task<MealPlanDto?> AcceptAsync(Guid userId, Guid planId);

    Task<bool> DeleteAsync(Guid userId, Guid planId);
}
