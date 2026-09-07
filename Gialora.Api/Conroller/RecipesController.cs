// Gialora.Api/Controllers/RecipesController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Api.Extensions;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecipesController : ControllerBase
{
    private readonly IRecipeService _recipeService;

    public RecipesController(IRecipeService recipeService)
    {
        _recipeService = recipeService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RecipeSummaryDto>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var recipes = await _recipeService.GetPublishedRecipesAsync(page, pageSize);
        return Ok(recipes);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RecipeDetailDto>> GetById(Guid id)
    {
        var recipe = await _recipeService.GetRecipeByIdAsync(id);
        return recipe is null ? NotFound() : Ok(recipe);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Create([FromBody] RecipeCreateDto dto)
    {
        // Նախ validation, հետո նոր claim-երի հետ աշխատանք
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (User.GetUserId() is not { } adminId)
            return Unauthorized();

        try
        {
            var id = await _recipeService.CreateRecipeAsync(dto, adminId);
            return CreatedAtAction(nameof(GetById), new { id }, id);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _recipeService.DeleteRecipeAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
