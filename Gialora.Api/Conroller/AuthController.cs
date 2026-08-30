// Gialora.Api/Controllers/AuthController.cs
using Microsoft.AspNetCore.Mvc;
using Gialora.Application.Services;
using Gialora.Api.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
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
    public async Task<ActionResult> Register([FromBody] RegisterDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var result = await _authService.RegisterAsync(dto);
            var token = _tokenGenerator.GenerateToken(result);
            return Ok(new { token, user = result });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message); // 409 — email-ն արդեն զբաղված է
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult> Login([FromBody] LoginDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _authService.ValidateCredentialsAsync(dto);

        if (result is null)
            return Unauthorized("Invalid email or password."); // Դիտավորյալ ընդհանուր message

        var token = _tokenGenerator.GenerateToken(result);
        return Ok(new { token, user = result });
    }
}