// Gialora.Client/Services/LanguageHeaderHandler.cs
using System.Net.Http.Headers;
using Gialora.Client.Localization;

namespace Gialora.Client.Services;

/// <summary>
/// Ամեն API request-ին կցում է UI-ի լեզուն (Accept-Language), որ API-ն
/// ռեցեպտները, բաղադրիչներն ու tag-երը վերադարձնի այդ լեզվով (fallback-ը՝ անգլերեն)։
///
/// Localizer-ը singleton է, ուստի այստեղ նույն օրինակն է, ինչ UI-ում — նույն
/// պատճառով, ինչ TokenStore-ը (տես AuthHeaderHandler-ի մեկնաբանությունը)։
/// </summary>
public class LanguageHeaderHandler : DelegatingHandler
{
    private readonly Localizer _localizer;

    public LanguageHeaderHandler(Localizer localizer)
    {
        _localizer = localizer;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.AcceptLanguage.Clear();
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(_localizer.Language));

        return base.SendAsync(request, cancellationToken);
    }
}
