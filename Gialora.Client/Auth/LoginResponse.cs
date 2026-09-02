using Gialora.Shared.Dtos;

namespace Gialora.Client.Auth;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public AuthResultDto User { get; set; } = null!;
}