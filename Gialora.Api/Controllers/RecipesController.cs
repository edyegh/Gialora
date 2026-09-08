// Gialora.Api/Controllers/RecipesController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Api.Extensions;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

/// <summary>
/// Product 1 — Recipe discovery (app structure §6)։
/// Կարդալը բաց է բոլորի համար (SEO/public catalog), գրելը՝ միայն admin-ի։
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RecipesController : ControllerBase
{
    private readonly IRecipeService _recipeService;
    private readonly IFeedbackService _feedbackService;

    public RecipesController(IRecipeService recipeService, IFeedbackService feedbackService)
    {
        _recipeService = recipeService;
        _feedbackService = feedbackService;
    }

    /// <summary>Public catalog with filters. Anonymous-ի համար IsFavorite-ը միշտ false է։</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<PagedResult<RecipeSummaryDto>>> Search([FromQuery] RecipeFilterDto filter)
    {
        // FavoritesOnly-ը իմաստ ունի միայն մուտք գործած user-ի համար
        var userId = User.GetUserId();
        if (filter.FavoritesOnly && userId is null)
            return Unauthorized(new ApiErrorDto { Message = "Sign in to filter by your favourites." });

        return Ok(await _recipeService.SearchAsync(filter, userId));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<RecipeDetailDto>> GetById(Guid id)
    {
        var recipe = await _recipeService.GetByIdAsync(id, User.GetUserId());
        return recipe is null ? NotFound() : Ok(recipe);
    }

    /// <summary>SEO-friendly ուղին՝ /api/recipes/by-slug/chicken-meatballs։</summary>
    [HttpGet("by-slug/{slug}")]
    [AllowAnonymous]
    public async Task<ActionResult<RecipeDetailDto>> GetBySlug(string slug)
    {
        var recipe = await _recipeService.GetBySlugAsync(slug, User.GetUserId());
        return recipe is null ? NotFound() : Ok(recipe);
    }

    [HttpGet("{id:guid}/ratings")]
    [AllowAnonymous]
    public async Task<ActionResult<RecipeRatingSummaryDto>> GetRatings(Guid id) =>
        Ok(await _feedbackService.GetSummaryAsync(id, User.GetUserId()));

    // -----------------------------------------------------------------------
    // Admin (app structure §11)
    // -----------------------------------------------------------------------

    /// <summary>Admin catalog — ներառում է draft-երը։</summary>
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PagedResult<RecipeSummaryDto>>> SearchForAdmin(
        [FromQuery] RecipeFilterDto filter, [FromQuery] bool? published) =>
        Ok(await _recipeService.SearchForAdminAsync(filter, published));

    /// <summary>Admin-ը պիտի կարողանա բացել նաև չհրապարակված ռեցեպտը՝ խմբագրելու համար։</summary>
    [HttpGet("admin/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RecipeDetailDto>> GetForAdmin(Guid id)
    {
        var recipe = await _recipeService.GetByIdAsync(id, User.GetUserId(), includeUnpublished: true);
        return recipe is null ? NotFound() : Ok(recipe);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<Guid>> Create([FromBody] RecipeCreateDto dto)
    {
        if (User.GetUserId() is not { } adminId)
            return Unauthorized();

        var id = await _recipeService.CreateAsync(dto, adminId);
        return CreatedAtAction(nameof(GetById), new { id }, id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] RecipeUpdateDto dto)
    {
        if (User.GetUserId() is not { } adminId)
            return Unauthorized();

        return await _recipeService.UpdateAsync(id, dto, adminId) ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Publish(Guid id) =>
        await _recipeService.SetPublishedAsync(id, true) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Unpublish(Guid id) =>
        await _recipeService.SetPublishedAsync(id, false) ? NoContent() : NotFound();

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id) =>
        await _recipeService.DeleteAsync(id) ? NoContent() : NotFound();
}
