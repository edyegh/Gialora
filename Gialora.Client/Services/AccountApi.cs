// Gialora.Client/Services/AccountApi.cs
using Gialora.Client.Auth;
using Gialora.Shared.Dtos;

namespace Gialora.Client.Services;

public class AccountApi : ApiClientBase
{
    private readonly TokenAuthStateProvider _authState;

    public AccountApi(HttpClient http, TokenAuthStateProvider authState) : base(http)
    {
        _authState = authState;
    }

    /// <summary>Հաջող մուտքից հետո token-ը անմիջապես պահում ենք — կանչողը դա չի մոռանա։</summary>
    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        var result = await PostAsync<AuthResponseDto>("api/auth/login", dto);
        await StoreAsync(result);
        return result;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
    {
        var result = await PostAsync<AuthResponseDto>("api/auth/register", dto);
        await StoreAsync(result);
        return result;
    }

    public Task LogoutAsync() => _authState.ClearTokenAsync();

    private async Task StoreAsync(AuthResponseDto? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Token))
            throw new ApiException(System.Net.HttpStatusCode.BadGateway,
                "The server returned an unexpected response. Please try again.");

        await _authState.SetTokenAsync(result.Token);
    }
}
