// Gialora.Api/Services/RequestContentLanguage.cs
using Gialora.Application.Common;
using Gialora.Shared;

namespace Gialora.Api.Services;

/// <summary>
/// Request-ի բովանդակության լեզուն։ Առաջնահերթությունը՝
///   1. ?lang=hy query-ն (link-երով կիսվելու, Swagger-ից ստուգելու համար)
///   2. Accept-Language header-ը (client-ը ամեն request-ի հետ ուղարկում է UI-ի լեզուն)
///   3. անգլերեն
/// HttpContext չլինելու դեպքում (seeder, background service)՝ անգլերեն։
/// </summary>
public sealed class RequestContentLanguage : IContentLanguage
{
    private readonly IHttpContextAccessor _accessor;
    private string? _code;

    public RequestContentLanguage(IHttpContextAccessor accessor) => _accessor = accessor;

    public string Code => _code ??= Resolve(_accessor.HttpContext?.Request);

    private static string Resolve(HttpRequest? request)
    {
        if (request is null)
            return ContentLanguages.Default;

        var fromQuery = request.Query["lang"].ToString();
        if (ContentLanguages.IsSupported(fromQuery))
            return fromQuery;

        // "hy-AM,hy;q=0.9,en;q=0.8" → ըստ q-ի, առաջին աջակցվողը
        var accepted = request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(l => l.Quality ?? 1d)
            .Select(l => l.Value.Value);

        foreach (var tag in accepted)
        {
            var primary = tag?.Split('-')[0].ToLowerInvariant();
            if (ContentLanguages.IsSupported(primary))
                return primary!;
        }

        return ContentLanguages.Default;
    }
}
