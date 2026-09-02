// Gialora.Client/Auth/AuthHeaderHandler.cs
using System.Net.Http.Headers;

namespace Gialora.Client.Auth;

public class AuthHeaderHandler : DelegatingHandler
{
    private readonly TokenAuthStateProvider _authStateProvider;

    public AuthHeaderHandler(TokenAuthStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _authStateProvider.GetTokenAsync();

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}