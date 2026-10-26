// Gialora.Client/Services/AccountApi.cs
using Gialora.Client.Localization;
using Gialora.Client.Auth;
using Gialora.Shared.Dtos;

namespace Gialora.Client.Services;

public class AccountApi : ApiClientBase
{
    private readonly TokenAuthStateProvider _authState;

    public AccountApi(HttpClient http, TokenAuthStateProvider authState, Localizer localizer) : base(http, localizer)
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

    /// <summary>Token չկա — account-ը ակտիվանում է միայն VerifyEmailAsync-ից հետո։</summary>
    public Task<RegisterResultDto> RegisterAsync(RegisterDto dto) =>
        PostAsync<RegisterResultDto>("api/auth/register", dto);

    /// <summary>Ճիշտ կոդ → token-ը պահում ենք, user-ը մուտք է գործած։</summary>
    public async Task<AuthResponseDto> VerifyEmailAsync(VerifyEmailDto dto)
    {
        var result = await PostAsync<AuthResponseDto>("api/auth/verify-email", dto);
        await StoreAsync(result);
        return result;
    }

    public Task ResendVerificationAsync(ResendVerificationDto dto) =>
        PostAsync("api/auth/resend-verification", dto);

    public Task LogoutAsync() => _authState.ClearTokenAsync();

    private async Task StoreAsync(AuthResponseDto? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Token))
            throw new ApiException(System.Net.HttpStatusCode.BadGateway,
                "The server returned an unexpected response. Please try again.");

        await _authState.SetTokenAsync(result.Token);
    }
}
