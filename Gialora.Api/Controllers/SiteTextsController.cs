// Gialora.Api/Controllers/SiteTextsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

/// <summary>
/// Admin-ի խմբագրած UI տեքստերը (գլխավոր էջ, footer…)։ Client-ը հանրային GET-ով
/// վերցնում է ընթացիկ լեզվի override-ները և դնում i18n JSON-ի վրա։
/// </summary>
[ApiController]
[Route("api/site-texts")]
public class SiteTextsController : ControllerBase
{
    private readonly ISiteTextService _texts;

    public SiteTextsController(ISiteTextService texts)
    {
        _texts = texts;
    }

    /// <summary>Բանալի → տեքստ մեկ լեզվի համար։ "admin"-ը literal route է և հաղթում է։</summary>
    [HttpGet("{language}")]
    [AllowAnonymous]
    public async Task<ActionResult<Dictionary<string, string>>> GetForLanguage(string language) =>
        Ok(await _texts.GetForLanguageAsync(language));

    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<SiteTextDto>>> GetAll() =>
        Ok(await _texts.GetAllAsync());

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SiteTextDto>> Save([FromBody] SiteTextDto dto) =>
        Ok(await _texts.SaveAsync(dto));

    // Բանալին query-ում է, ոչ թե path-ում — "Home.Heading"-ի կետը path-ի վերջում
    // որոշ host-եր (IIS) ֆայլի extension են համարում
    [HttpDelete("{language}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reset(string language, [FromQuery] string key) =>
        await _texts.ResetAsync(key, language) ? NoContent() : NotFound();
}
