// Gialora.Api/Controllers/FamilyController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // ← ցանկացած logged-in user (ոչ միայն Admin) — Family-ն իր սեփականն է կառավարում
public class FamilyController : ControllerBase
{
    private readonly IFamilyService _familyService;

    public FamilyController(IFamilyService familyService)
    {
        _familyService = familyService;
    }

    [HttpGet("me")]
    public async Task<ActionResult<FamilyDto>> GetMyFamily()
    {
        var userId = GetCurrentUserId();
        var family = await _familyService.GetOrCreateFamilyAsync(userId);
        return Ok(family);
    }

    [HttpPost("members")]
    public async Task<ActionResult<FamilyMemberDto>> AddMember([FromBody] FamilyMemberCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        var member = await _familyService.AddMemberAsync(userId, dto);
        return CreatedAtAction(nameof(GetMyFamily), member);
    }

    [HttpPut("members/{memberId:guid}")]
    public async Task<ActionResult<FamilyMemberDto>> UpdateMember(Guid memberId, [FromBody] FamilyMemberUpdateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        var updated = await _familyService.UpdateMemberAsync(userId, memberId, dto);

        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid memberId)
    {
        var userId = GetCurrentUserId();
        var removed = await _familyService.RemoveMemberAsync(userId, memberId);

        return removed ? NoContent() : NotFound();
    }

    private Guid GetCurrentUserId()
    {
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return Guid.Parse(idClaim!);
    }
}