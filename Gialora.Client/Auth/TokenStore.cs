// Gialora.Client/Auth/TokenStore.cs
using Microsoft.JSInterop;

namespace Gialora.Client.Auth;

/// <summary>
/// Ամբողջ հավելվածի ՄԻԱԿ token-ի աղբյուրը (singleton)։
///
/// Ինչու առանձին, և ոչ թե TokenAuthStateProvider-ի ներսում.
/// IHttpClientFactory-ն handler-ների շղթան կառուցում է ԻՐ ՍԵՓԱԿԱՆ scope-ում
/// (DefaultHttpClientFactory.CreateHandlerEntry → _scopeFactory.CreateScope()),
/// ուստի AuthHeaderHandler-ը ստանում էր scoped ծառայությունների ՈՒՐԻՇ օրինակներ,
/// քան UI-ի բաղադրիչները։
///
/// Հետևանքը իրական bug էր. գրանցվելուց հետո UI-ն ցույց էր տալիս "մուտք գործած",
/// բայց handler-ի օրինակի cache-ը պահում էր login-ից ԱՌԱՋ կարդացած null-ը
/// (_tokenLoaded = true, _cachedToken = null), Authorization header չէր ավելացնում,
/// և GET /api/family/me-ն վերադարձնում էր 401 → "Please sign in to continue."։
///
/// Singleton-ը բոլոր scope-երում նույն օրինակն է, ուստի խնդիրը վերանում է արմատից։
/// </summary>
public class TokenStore
{
    private const string TokenKey = "authToken";

    private readonly IJSRuntime _jsRuntime;

    private string? _cachedToken;
    private bool _loaded;

    public TokenStore(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>Token-ը փոխվեց (մուտք/ելք)։ TokenAuthStateProvider-ը լսում է սա։</summary>
    public event Action? Changed;

    public async ValueTask<string?> GetAsync()
    {
        // Cache — առանց դրա ամեն HTTP request localStorage-ի JS interop կանչ էր անում
        if (_loaded)
            return _cachedToken;

        try
        {
            _cachedToken = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", TokenKey);
        }
        catch (JSException)
        {
            // Արգելափակված storage — անանուն ենք համարում, էջը չպիտի կոտրվի
            _cachedToken = null;
        }

        _loaded = true;
        return _cachedToken;
    }

    public async Task SetAsync(string token)
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
        _cachedToken = token;
        _loaded = true;
        Changed?.Invoke();
    }

    public async Task ClearAsync()
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        _cachedToken = null;
        _loaded = true;
        Changed?.Invoke();
    }
}
