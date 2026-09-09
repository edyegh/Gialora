// Gialora.Tests/AuthTokenFlowTests.cs
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Gialora.Client.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.JSInterop;

namespace Gialora.Tests;

/// <summary>
/// Regression tests for the reported bug: right after creating an account the
/// family page showed "Please sign in to continue." while the navigation bar
/// still showed the user as signed in.
///
/// Cause: IHttpClientFactory builds its handler chain inside a DI scope of its
/// own, so AuthHeaderHandler received a *different* instance of the token cache
/// than the UI did. That instance had already cached "no token" from a request
/// made before the user registered, so no Authorization header was ever
/// attached and the API answered 401.
/// </summary>
public class AuthTokenFlowTests
{
    /// <summary>Records the outgoing Authorization header so tests can assert on it.</summary>
    private sealed class SpyHandler : HttpMessageHandler
    {
        public string? SeenAuthorization { get; private set; }
        public HttpStatusCode StatusToReturn { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SeenAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(StatusToReturn));
        }
    }

    /// <summary>An in-memory stand-in for the browser's localStorage.</summary>
    private sealed class FakeJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> _storage = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var key = args?.ElementAtOrDefault(0)?.ToString() ?? string.Empty;

            switch (identifier)
            {
                case "localStorage.getItem":
                    var value = _storage.TryGetValue(key, out var stored) ? stored : null;
                    return ValueTask.FromResult((TValue)(object?)value!);

                case "localStorage.setItem":
                    _storage[key] = args?.ElementAtOrDefault(1)?.ToString() ?? string.Empty;
                    return ValueTask.FromResult(default(TValue)!);

                case "localStorage.removeItem":
                    _storage.Remove(key);
                    return ValueTask.FromResult(default(TValue)!);

                default:
                    return ValueTask.FromResult(default(TValue)!);
            }
        }
    }

    /// <summary>
    /// A real, parseable JWT. The provider deliberately discards tokens it cannot
    /// read, so a placeholder string would be cleared the moment it is stored.
    /// </summary>
    private static string CreateJwt(string name = "Test Parent", int minutesValid = 60)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 32)));
        var handler = new JwtSecurityTokenHandler();
        handler.OutboundClaimTypeMap.Clear();

        var token = new JwtSecurityToken(
            issuer: "Gialora.Api",
            audience: "Gialora.Client",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim("name", name),
                new Claim("role", "User")
            },
            expires: DateTime.UtcNow.AddMinutes(minutesValid),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return handler.WriteToken(token);
    }

    /// <summary>Wires up services the way Gialora.Client/Program.cs does.</summary>
    private static ServiceProvider BuildClientServices(SpyHandler spy, bool tokenStoreAsSingleton = true)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IJSRuntime, FakeJsRuntime>();

        if (tokenStoreAsSingleton)
            services.AddSingleton<TokenStore>();
        else
            services.AddScoped<TokenStore>(); // the broken shape, kept for the regression test

        services.AddScoped<AuthHeaderHandler>();
        services.AddScoped<TokenAuthStateProvider>();

        services.AddHttpClient("GialoraApi", client => client.BaseAddress = new Uri("https://localhost:5001/"))
            .AddHttpMessageHandler<AuthHeaderHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => spy);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Token_store_is_shared_with_the_http_handler_scope()
    {
        using var provider = BuildClientServices(new SpyHandler());

        var fromUi = provider.GetRequiredService<TokenStore>();

        // This mirrors the scope IHttpClientFactory creates for its handler chain.
        using var handlerScope = provider.CreateScope();
        var fromHandler = handlerScope.ServiceProvider.GetRequiredService<TokenStore>();

        Assert.Same(fromUi, fromHandler);
    }

    [Fact]
    public async Task Register_then_open_the_family_page_sends_the_bearer_token()
    {
        var spy = new SpyHandler();
        using var provider = BuildClientServices(spy);

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("GialoraApi");

        // 1. Anonymous browsing. This is the request that poisoned the handler's
        //    cache before the fix.
        await client.GetAsync("api/recipes");
        Assert.Null(spy.SeenAuthorization);

        // 2. The user registers; AccountApi stores the token via the UI's provider.
        var jwt = CreateJwt();
        await provider.GetRequiredService<TokenAuthStateProvider>().SetTokenAsync(jwt);

        // 3. The family page loads. This is the request that returned 401.
        await client.GetAsync("api/family/me");

        Assert.Equal($"Bearer {jwt}", spy.SeenAuthorization);
    }

    [Fact]
    public async Task Regression_a_scoped_token_store_loses_the_token_after_anonymous_browsing()
    {
        var spy = new SpyHandler();
        using var provider = BuildClientServices(spy, tokenStoreAsSingleton: false);

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("GialoraApi");

        await client.GetAsync("api/recipes");
        await provider.GetRequiredService<TokenAuthStateProvider>().SetTokenAsync(CreateJwt());
        await client.GetAsync("api/family/me");

        // This is precisely the reported failure: signed in according to the UI,
        // but no credentials on the wire, so the API answers 401.
        Assert.Null(spy.SeenAuthorization);
    }

    [Fact]
    public async Task The_authentication_state_reflects_the_token()
    {
        using var provider = BuildClientServices(new SpyHandler());
        var authState = provider.GetRequiredService<TokenAuthStateProvider>();

        Assert.False((await authState.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);

        await authState.SetTokenAsync(CreateJwt(name: "Test Parent"));

        var state = await authState.GetAuthenticationStateAsync();
        Assert.True(state.User.Identity?.IsAuthenticated);
        Assert.Equal("Test Parent", state.User.Identity?.Name);
        Assert.True(state.User.IsInRole("User"));
    }

    [Fact]
    public async Task An_expired_token_is_discarded_rather_than_shown_as_a_session()
    {
        using var provider = BuildClientServices(new SpyHandler());
        var authState = provider.GetRequiredService<TokenAuthStateProvider>();
        var tokens = provider.GetRequiredService<TokenStore>();

        await tokens.SetAsync(CreateJwt(minutesValid: -5));

        Assert.False((await authState.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);
        Assert.Null(await tokens.GetAsync());
    }

    [Fact]
    public async Task Signing_out_stops_sending_the_token()
    {
        var spy = new SpyHandler();
        using var provider = BuildClientServices(spy);

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("GialoraApi");
        var authState = provider.GetRequiredService<TokenAuthStateProvider>();

        var jwt = CreateJwt();
        await authState.SetTokenAsync(jwt);
        await client.GetAsync("api/family/me");
        Assert.Equal($"Bearer {jwt}", spy.SeenAuthorization);

        await authState.ClearTokenAsync();
        await client.GetAsync("api/recipes");

        Assert.Null(spy.SeenAuthorization);
    }

    [Fact]
    public async Task A_rejected_token_is_cleared_so_the_ui_stops_claiming_a_session()
    {
        var spy = new SpyHandler();
        using var provider = BuildClientServices(spy);

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("GialoraApi");
        var tokens = provider.GetRequiredService<TokenStore>();

        await tokens.SetAsync(CreateJwt());

        spy.StatusToReturn = HttpStatusCode.Unauthorized;
        await client.GetAsync("api/family/me");

        // Without this the navigation bar keeps showing a signed-in user while
        // every page underneath reports "Please sign in to continue."
        Assert.Null(await tokens.GetAsync());
    }
}
