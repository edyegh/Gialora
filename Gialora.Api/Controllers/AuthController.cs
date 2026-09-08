// Gialora.Api/Controllers/AuthController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Application.Services;
using Gialora.Api.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
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
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var result = await _authService.RegisterAsync(dto);
            return Ok(BuildResponse(result));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message); // 409 — email-ն արդեն զբաղված է
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _authService.ValidateCredentialsAsync(dto);

        if (result is null)
            return Unauthorized("Invalid email or password."); // Դիտավորյալ ընդհանուր message

        return Ok(BuildResponse(result));
    }

    private AuthResponseDto BuildResponse(AuthResultDto user) =>
        new() { Token = _tokenGenerator.GenerateToken(user), User = user };
}
