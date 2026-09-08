// Gialora.Api/Controllers/MealPlansController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Api.Extensions;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

/// <summary>
/// Product 2 — Personal meal planner (app structure §6)։ Սա է հավելվածի
/// տարբերակիչ արժեքը՝ "we help busy families decide what to cook"։
/// </summary>
[ApiController]
[Route("api/mealplans")]
[Authorize]
public class MealPlansController : ControllerBase
{
    private readonly IMealPlanService _mealPlans;
    private readonly IShoppingListService _shoppingLists;

    public MealPlansController(IMealPlanService mealPlans, IShoppingListService shoppingLists)
    {
        _mealPlans = mealPlans;
        _shoppingLists = shoppingLists;
    }

    [HttpGet]
    public async Task<ActionResult<List<MealPlanDto>>> GetMine()
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return Ok(await _mealPlans.GetMyPlansAsync(userId));
    }

    /// <summary>Home էջի "your week" widget-ը սա է կանչում։</summary>
    [HttpGet("current")]
    public async Task<ActionResult<MealPlanDto>> GetCurrent()
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var plan = await _mealPlans.GetCurrentAsync(userId);
        return plan is null ? NoContent() : Ok(plan);
    }

    [HttpGet("{planId:guid}")]
    public async Task<ActionResult<MealPlanDto>> GetById(Guid planId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var plan = await _mealPlans.GetByIdAsync(userId, planId);
        return plan is null ? NotFound() : Ok(plan);
    }

    [HttpPost("generate")]
    public async Task<ActionResult<MealPlanDto>> Generate([FromBody] MealPlanGenerateDto dto)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var plan = await _mealPlans.GenerateAsync(userId, dto);
        return CreatedAtAction(nameof(GetById), new { planId = plan.Id }, plan);
    }

    /// <summary>"Replace one meal" — body-ում recipeId կամ null (engine-ը ընտրում է)։</summary>
    [HttpPost("{planId:guid}/entries/{entryId:guid}/swap")]
    public async Task<ActionResult<MealPlanDto>> SwapEntry(
        Guid planId, Guid entryId, [FromBody] MealPlanSwapDto dto)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var plan = await _mealPlans.SwapEntryAsync(userId, planId, entryId, dto);
        return plan is null ? NotFound() : Ok(plan);
    }

    [HttpPost("{planId:guid}/entries/{entryId:guid}/lock")]
    public async Task<ActionResult<MealPlanDto>> Lock(Guid planId, Guid entryId, [FromQuery] bool locked = true)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var plan = await _mealPlans.SetEntryLockedAsync(userId, planId, entryId, locked);
        return plan is null ? NotFound() : Ok(plan);
    }

    /// <summary>"Replace an entire day"։</summary>
    [HttpPost("{planId:guid}/days/{dayId:guid}/regenerate")]
    public async Task<ActionResult<MealPlanDto>> RegenerateDay(Guid planId, Guid dayId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var plan = await _mealPlans.RegenerateDayAsync(userId, planId, dayId);
        return plan is null ? NotFound() : Ok(plan);
    }

    /// <summary>"Regenerate the week"։</summary>
    [HttpPost("{planId:guid}/regenerate")]
    public async Task<ActionResult<MealPlanDto>> Regenerate(Guid planId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var plan = await _mealPlans.RegenerateAsync(userId, planId);
        return plan is null ? NotFound() : Ok(plan);
    }

    /// <summary>"Accept it"։</summary>
    [HttpPost("{planId:guid}/accept")]
    public async Task<ActionResult<MealPlanDto>> Accept(Guid planId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var plan = await _mealPlans.AcceptAsync(userId, planId);
        return plan is null ? NotFound() : Ok(plan);
    }

    /// <summary>Ավտոմատ գնումների ցանկը այս պլանից (app structure §1)։</summary>
    [HttpPost("{planId:guid}/shopping-list")]
    public async Task<ActionResult<ShoppingListDto>> GenerateShoppingList(Guid planId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return Ok(await _shoppingLists.GenerateFromMealPlanAsync(userId, planId));
    }

    [HttpGet("{planId:guid}/shopping-list")]
    public async Task<ActionResult<ShoppingListDto>> GetShoppingList(Guid planId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var list = await _shoppingLists.GetForMealPlanAsync(userId, planId);
        return list is null ? NotFound() : Ok(list);
    }

    [HttpDelete("{planId:guid}")]
    public async Task<IActionResult> Delete(Guid planId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return await _mealPlans.DeleteAsync(userId, planId) ? NoContent() : NotFound();
    }
}
