// Gialora.Api/Controllers/IngredientsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngredientsController : ControllerBase
{
    private readonly IIngredientService _ingredients;

    public IngredientsController(IIngredientService ingredients)
    {
        _ingredients = ingredients;
    }

    /// <summary>Կատալոգը բաց է — recipe filter-ը և admin editor-ը երկուսն էլ սա են կանչում։</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<IngredientDto>>> GetAll([FromQuery] string? search) =>
        Ok(await _ingredients.GetAllAsync(search));

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<IngredientDto>> GetById(Guid id)
    {
        var ingredient = await _ingredients.GetByIdAsync(id);
        return ingredient is null ? NotFound() : Ok(ingredient);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IngredientDto>> Create([FromBody] IngredientCreateDto dto)
    {
        var created = await _ingredients.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IngredientDto>> Update(Guid id, [FromBody] IngredientUpdateDto dto)
    {
        var updated = await _ingredients.UpdateAsync(id, dto);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id) =>
        await _ingredients.DeleteAsync(id) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/tags")]
[AllowAnonymous]
public class TagsController : ControllerBase
{
    private readonly IIngredientService _ingredients;

    public TagsController(IIngredientService ingredients)
    {
        _ingredients = ingredients;
    }

    [HttpGet]
    public async Task<ActionResult<List<TagDto>>> GetAll() =>
        Ok(await _ingredients.GetTagsAsync());
}
