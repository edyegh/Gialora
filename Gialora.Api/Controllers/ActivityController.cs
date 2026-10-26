// Gialora.Api/Controllers/ActivityController.cs
using Gialora.Api.Extensions;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Gialora.Api.Controllers;

/// <summary>
/// Browser-ը այստեղ է ուղարկում էջի դիտումները / click-երը (batch-երով)։ Բաց է հյուրերի
/// համար էլ — մուտք գործածին ճանաչում ենք JWT-ով։ Client-ը ուղարկում է միայն
/// cookie-ների "Accept all"-ից հետո։
/// </summary>
[ApiController]
[Route("api/activity")]
[AllowAnonymous]
public class ActivityController : ControllerBase
{
    private readonly IActivityService _activity;

    public ActivityController(IActivityService activity) => _activity = activity;

    [HttpPost]
    [EnableRateLimiting("activity")]
    public async Task<IActionResult> Record([FromBody] ActivityBatchDto batch)
    {
        await _activity.RecordAsync(User.GetUserId(), batch);
        return NoContent();
    }
}
