// Gialora.Api/Controllers/AdminStatsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

/// <summary>Admin-ի վիճակագրությունը՝ ինչ են user-ները հավանում, սեղմում, անում։</summary>
[ApiController]
[Route("api/admin/stats")]
[Authorize(Roles = "Admin")]
public class AdminStatsController : ControllerBase
{
    private readonly IActivityService _activity;

    public AdminStatsController(IActivityService activity) => _activity = activity;

    [HttpGet]
    public async Task<ActionResult<AdminStatsDto>> Get([FromQuery] int days = 30) =>
        Ok(await _activity.GetStatsAsync(days));

    [HttpGet("users/{id:guid}")]
    public async Task<ActionResult<AdminUserActivityDto>> GetUser(Guid id, [FromQuery] int days = 30) =>
        Ok(await _activity.GetUserActivityAsync(id, days));
}
