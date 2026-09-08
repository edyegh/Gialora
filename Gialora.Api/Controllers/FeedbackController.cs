// Gialora.Api/Controllers/FeedbackController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Api.Extensions;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

/// <summary>
/// Feedback loop-ը (app structure §1)։ Այս տվյալը ուղղակիորեն ազդում է
/// հաջորդ meal plan-ի վրա — տես MealPlanService.LoadHistoryScoresAsync։
/// </summary>
[ApiController]
[Route("api/feedback")]
[Authorize]
public class FeedbackController : ControllerBase
{
    private readonly IFeedbackService _feedback;

    public FeedbackController(IFeedbackService feedback)
    {
        _feedback = feedback;
    }

    [HttpGet("mine")]
    public async Task<ActionResult<List<FeedbackDto>>> GetMine()
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return Ok(await _feedback.GetMyFeedbackAsync(userId));
    }

    [HttpPut("recipes/{recipeId:guid}")]
    public async Task<ActionResult<FeedbackDto>> Upsert(Guid recipeId, [FromBody] FeedbackUpsertDto dto)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return Ok(await _feedback.UpsertAsync(userId, recipeId, dto));
    }

    [HttpDelete("recipes/{recipeId:guid}")]
    public async Task<IActionResult> Delete(Guid recipeId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return await _feedback.DeleteAsync(userId, recipeId) ? NoContent() : NotFound();
    }
}

[ApiController]
[Route("api/favorites")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly IFavoriteService _favorites;

    public FavoritesController(IFavoriteService favorites)
    {
        _favorites = favorites;
    }

    [HttpGet]
    public async Task<ActionResult<List<RecipeSummaryDto>>> GetMine()
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return Ok(await _favorites.GetMyFavoritesAsync(userId));
    }

    /// <summary>Idempotent — երկրորդ անգամ ավելացնելը սխալ չէ։</summary>
    [HttpPut("{recipeId:guid}")]
    public async Task<IActionResult> Add(Guid recipeId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        await _favorites.AddAsync(userId, recipeId);
        return NoContent();
    }

    [HttpDelete("{recipeId:guid}")]
    public async Task<IActionResult> Remove(Guid recipeId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return await _favorites.RemoveAsync(userId, recipeId) ? NoContent() : NotFound();
    }
}
