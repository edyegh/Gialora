// Gialora.Api/Controllers/AdminUsersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Api.Extensions;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

/// <summary>Admin-ի user management-ը՝ ցուցակ, ավելացնել, password փոխել, հեռացնել։</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public class AdminUsersController : ControllerBase
{
    private readonly IUserAdminService _users;

    public AdminUsersController(IUserAdminService users)
    {
        _users = users;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminUserDto>>> List()
    {
        if (User.GetUserId() is not { } adminId)
            return Unauthorized();

        return Ok(await _users.ListAsync(adminId));
    }

    [HttpPost]
    public async Task<ActionResult<AdminUserDto>> Create([FromBody] AdminUserCreateDto dto)
    {
        if (User.GetUserId() is not { } adminId)
            return Unauthorized();

        // ConflictException/ValidationFailedException-ը ճիշտ status-ի է վերածում middleware-ը
        var created = await _users.CreateAsync(dto, adminId);
        return Ok(created);
    }

    [HttpPut("{id:guid}/password")]
    public async Task<IActionResult> SetPassword(Guid id, [FromBody] AdminSetPasswordDto dto)
    {
        await _users.SetPasswordAsync(id, dto);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (User.GetUserId() is not { } adminId)
            return Unauthorized();

        await _users.DeleteAsync(id, adminId);
        return NoContent();
    }
}
