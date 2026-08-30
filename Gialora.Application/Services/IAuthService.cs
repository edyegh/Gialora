

using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IAuthService
{
    Task<AuthResultDto> RegisterAsync(RegisterDto dto);
    Task<AuthResultDto?> ValidateCredentialsAsync(LoginDto dto);
}