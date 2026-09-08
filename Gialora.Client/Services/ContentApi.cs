// Gialora.Client/Services/ContentApi.cs
using Gialora.Shared.Dtos;

namespace Gialora.Client.Services;

/// <summary>
/// Blog — "Tips, guides &amp; Mediterranean inspiration" (app structure §2)։
///
/// Enum-ների dropdown-ները ԱՅՍՏԵՂ ՉԵՆ. Blazor client-ը reference է անում
/// Gialora.Shared-ը, ուստի Enum.GetValues&lt;T&gt;()-ը և՛ ավելի արագ է (ցանց չկա),
/// և՛ ավելի ապահով (compile-time)։ GET /api/meta-ն մնում է այլ լեզվով գրված
/// consumer-ների համար (app structure §8՝ մեկ backend, մի քանի front-end)։
/// </summary>
public class ContentApi : ApiClientBase
{
    public ContentApi(HttpClient http) : base(http) { }

    public Task<PagedResult<BlogPostSummaryDto>> GetPostsAsync(int page = 1, int pageSize = 10) =>
        GetAsync<PagedResult<BlogPostSummaryDto>>($"api/blog?page={page}&pageSize={pageSize}");

    public Task<BlogPostDetailDto?> GetPostAsync(string slug) =>
        GetOrDefaultAsync<BlogPostDetailDto>($"api/blog/{Uri.EscapeDataString(slug)}");
}
