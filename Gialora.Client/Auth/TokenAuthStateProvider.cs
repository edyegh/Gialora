// Gialora.Client/Auth/TokenAuthStateProvider.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Gialora.Client.Auth;

public class TokenAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    // API-ի JwtTokenGenerator-ը հենց այս անուններով է claim-երը գրում։
    // Առանց դրանք ClaimsIdentity-ին հայտնելու՝ User.Identity.Name-ը null է,
    // իսկ <AuthorizeView Roles="Admin"> / IsInRole()-ը՝ միշտ false։
    private const string NameClaimType = "name";
    private const string RoleClaimType = "role";

    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly TokenStore _tokens;

    public TokenAuthStateProvider(TokenStore tokens)
    {
        _tokens = tokens;

        // Token-ը կարող է փոխվել ուրիշ scope-ից (օր. AccountApi-ից) — UI-ն
        // պիտի իմանա այդ մասին, ուստի բաժանորդագրվում ենք store-ի event-ին։
        _tokens.Changed += OnTokenChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _tokens.GetAsync();

        if (string.IsNullOrWhiteSpace(token))
            return Anonymous;

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

            // Ժամկետանց token-ը մաքրում ենք, որ UI-ն "մուտք գործած" չձևանա
            if (jwt.ValidTo <= DateTime.UtcNow)
            {
                await _tokens.ClearAsync();
                return Anonymous;
            }

            var identity = new ClaimsIdentity(jwt.Claims, "jwt", NameClaimType, RoleClaimType);
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch (Exception)
        {
            // Token-ը վնասված է
            await _tokens.ClearAsync();
            return Anonymous;
        }
    }

    public Task SetTokenAsync(string token) => _tokens.SetAsync(token);

    public Task ClearTokenAsync() => _tokens.ClearAsync();

    public ValueTask<string?> GetTokenAsync() => _tokens.GetAsync();

    private void OnTokenChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose() => _tokens.Changed -= OnTokenChanged;
}
