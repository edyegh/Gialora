// Gialora.Client/Auth/AuthHeaderHandler.cs
using System.Net.Http.Headers;

namespace Gialora.Client.Auth;

/// <summary>
/// Կցում է bearer token-ը ամեն ելքային request-ին։
///
/// Կախվածությունը ՄԻԱՅՆ TokenStore-ից է (singleton), ոչ թե scoped
/// TokenAuthStateProvider-ից. IHttpClientFactory-ն այս handler-ը ստեղծում է
/// առանձին scope-ում, ուստի scoped ծառայությունը այստեղ ՈՒՐԻՇ օրինակ կլիներ,
/// քան UI-ինը, և token-ը երբեք չէր հասնի header-ին։
/// </summary>
public class AuthHeaderHandler : DelegatingHandler
{
    private readonly TokenStore _tokens;

    public AuthHeaderHandler(TokenStore tokens)
    {
        _tokens = tokens;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAsync();

        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        // Token-ը մերժվեց (ժամկետանց/չեղարկված) — մաքրում ենք, որ UI-ն դադարի
        // "մուտք գործած" ձևանալ։ Առանց սրա էջը ցույց էր տալիս անվանումդ nav-ում,
        // իսկ բովանդակության տեղում՝ "Please sign in to continue."։
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && token is not null)
            await _tokens.ClearAsync();

        return response;
    }
}
