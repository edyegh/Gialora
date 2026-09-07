// Gialora.Client/Auth/TokenAuthStateProvider.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Gialora.Client.Auth;

public class TokenAuthStateProvider : AuthenticationStateProvider
{
    private const string TokenKey = "authToken";

    // API-ի JwtTokenGenerator-ը հենց այս անուններով է claim-երը գրում։
    // Առանց դրանք ClaimsIdentity-ին հայտնելու՝ context.User.Identity.Name-ը null է,
    // իսկ <AuthorizeView Roles="Admin"> / IsInRole()-ը՝ միշտ false։
    private const string NameClaimType = "name";
    private const string RoleClaimType = "role";

    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly IJSRuntime _jsRuntime;

    // Cache — AuthHeaderHandler-ը ամեն HTTP request-ի համար էր localStorage կարդում
    private string? _cachedToken;
    private bool _tokenLoaded;

    public TokenAuthStateProvider(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await GetTokenAsync();

        if (string.IsNullOrWhiteSpace(token))
            return Anonymous;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            // Ստուգում ենք, արդյոք token-ը արդեն expired է
            if (jwt.ValidTo <= DateTime.UtcNow)
            {
                await ClearTokenAsync();
                return Anonymous;
            }

            var identity = new ClaimsIdentity(jwt.Claims, "jwt", NameClaimType, RoleClaimType);
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch (Exception)
        {
            // Token-ը corrupted/invalid է
            await ClearTokenAsync();
            return Anonymous;
        }
    }

    public async Task SetTokenAsync(string token)
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
        _cachedToken = token;
        _tokenLoaded = true;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task ClearTokenAsync()
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        _cachedToken = null;
        _tokenLoaded = true;
        // Ուշադրություն. այստեղ GetAuthenticationStateAsync()-ը կանչելը ռեկուրսիա կտար,
        // քանի որ ClearTokenAsync-ն ինքն էլ կանչվում է դրա միջից
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }

    public async Task<string?> GetTokenAsync()
    {
        if (_tokenLoaded)
            return _cachedToken;

        _cachedToken = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", TokenKey);
        _tokenLoaded = true;
        return _cachedToken;
    }
}
