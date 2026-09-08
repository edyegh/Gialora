// Gialora.Application/Services/IAuthService.cs
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IAuthService
{
    Task<AuthResultDto> RegisterAsync(RegisterDto dto);
    Task<AuthResultDto?> ValidateCredentialsAsync(LoginDto dto);

    /// <summary>Token-ի վավերականությունը ստուգելու համար (GET /api/auth/me)։</summary>
    Task<AuthResultDto?> GetByIdAsync(Guid userId);
}
