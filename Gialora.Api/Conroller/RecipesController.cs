// Gialora.Api/Controllers/RecipesController.cs
using Microsoft.AspNetCore.Mvc;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;
using Gialora.Application;
using Microsoft.AspNetCore.Authorization;

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
    [Authorize(Roles = "Admin")] // ← ԱՎԵԼԱՑՎԱԾ
    public async Task<ActionResult> Create([FromBody] RecipeCreateDto dto)
    {
        // Հիմա կարող ենք վերցնել իրական admin Id-ն token-ից, ոչ թե Guid.Empty
        var adminIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        var adminId = Guid.Parse(adminIdClaim!);

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

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
    [Authorize(Roles = "Admin")] // ← ԱՎԵԼԱՑՎԱԾ
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _recipeService.DeleteRecipeAsync(id);
        return deleted ? NoContent() : NotFound();

    }
}