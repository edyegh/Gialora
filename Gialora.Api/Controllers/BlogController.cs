// Gialora.Api/Controllers/BlogController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Api.Extensions;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

[ApiController]
[Route("api/blog")]
public class BlogController : ControllerBase
{
    private readonly IBlogService _blog;

    public BlogController(IBlogService blog)
    {
        _blog = blog;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<PagedResult<BlogPostSummaryDto>>> GetPublished(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10) =>
        Ok(await _blog.GetPublishedAsync(page, pageSize));

    [HttpGet("{slug}")]
    [AllowAnonymous]
    public async Task<ActionResult<BlogPostDetailDto>> GetBySlug(string slug)
    {
        var post = await _blog.GetBySlugAsync(slug);
        return post is null ? NotFound() : Ok(post);
    }

    // --- Admin ---

    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<BlogPostSummaryDto>>> GetAllForAdmin() =>
        Ok(await _blog.GetAllForAdminAsync());

    [HttpGet("admin/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BlogPostDetailDto>> GetForAdmin(Guid id)
    {
        var post = await _blog.GetByIdAsync(id);
        return post is null ? NotFound() : Ok(post);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<Guid>> Create([FromBody] BlogPostCreateDto dto)
    {
        if (User.GetUserId() is not { } authorId)
            return Unauthorized();

        var id = await _blog.CreateAsync(dto, authorId);
        return CreatedAtAction(nameof(GetForAdmin), new { id }, id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] BlogPostUpdateDto dto) =>
        await _blog.UpdateAsync(id, dto) ? NoContent() : NotFound();

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id) =>
        await _blog.DeleteAsync(id) ? NoContent() : NotFound();
}
