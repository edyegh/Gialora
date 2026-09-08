// Gialora.Api/Controllers/FamilyController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Api.Extensions;
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
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var family = await _familyService.GetOrCreateFamilyAsync(userId);
        return Ok(family);
    }

    [HttpPost("members")]
    public async Task<ActionResult<FamilyMemberDto>> AddMember([FromBody] FamilyMemberCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var member = await _familyService.AddMemberAsync(userId, dto);
        // routeValues-ը պարտադիր է — առանց դրա CreatedAtAction-ը Location header չի կարողանում կառուցել և նետում է InvalidOperationException
        return CreatedAtAction(nameof(GetMyFamily), null, member);
    }

    [HttpPut("members/{memberId:guid}")]
    public async Task<ActionResult<FamilyMemberDto>> UpdateMember(Guid memberId, [FromBody] FamilyMemberUpdateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var updated = await _familyService.UpdateMemberAsync(userId, memberId, dto);

        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid memberId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var removed = await _familyService.RemoveMemberAsync(userId, memberId);

        return removed ? NoContent() : NotFound();
    }
}
