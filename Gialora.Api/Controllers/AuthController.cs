// Gialora.Api/Controllers/AuthController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Gialora.Api.Extensions;
using Gialora.Api.Services;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")] // IP-ի մակարդակի brute-force պաշտպանություն
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IJwtTokenGenerator _tokenGenerator;

    public AuthController(IAuthService authService, IJwtTokenGenerator tokenGenerator)
    {
        _authService = authService;
        _tokenGenerator = tokenGenerator;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<RegisterResultDto>> Register([FromBody] RegisterDto dto)
    {
        // ConflictException-ը 409-ի է վերածում middleware-ը — այստեղ try/catch պետք չէ։
        // Token ՉԵՆՔ տալիս — նախ email-ի կոդը (verify-email)։
        var result = await _authService.RegisterAsync(dto);
        return Ok(result);
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> VerifyEmail([FromBody] VerifyEmailDto dto)
    {
        // Սխալ/ժամկետանց կոդը ValidationFailedException է → 400 (middleware)
        var result = await _authService.VerifyEmailAsync(dto);
        return Ok(BuildResponse(result));
    }

    [HttpPost("resend-verification")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationDto dto)
    {
        // Միշտ 204 — պատասխանից չպիտի երևա՝ email-ը գրանցված է, թե ոչ
        await _authService.ResendVerificationAsync(dto);
        return NoContent();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
    {
        var result = await _authService.ValidateCredentialsAsync(dto);

        if (result is null)
        {
            // Դիտավորյալ ընդհանուր message — չպիտի բացահայտենք՝ email-ը գոյություն ունի՞
            return Unauthorized(new ApiErrorDto { Message = "Invalid email or password." });
        }

        return Ok(BuildResponse(result));
    }

    /// <summary>Client-ը սա կանչում է token-ի վավերականությունը ստուգելու համար։</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthResultDto>> Me()
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var user = await _authService.GetByIdAsync(userId);
        return user is null ? Unauthorized() : Ok(user);
    }

    private AuthResponseDto BuildResponse(AuthResultDto user) =>
        new() { Token = _tokenGenerator.GenerateToken(user), User = user };
}
