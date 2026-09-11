// Gialora.Api/Controllers/AdminImportController.cs
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Api.Extensions;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

/// <summary>
/// Bulk recipe upload (app structure §12)։ "Can we create a structured recipe-import
/// template so I can upload recipes in bulk?" — այո, ահա այն։
/// </summary>  
[ApiController]
[Route("api/admin/import")]
[Authorize(Roles = "Admin")]
public class AdminImportController : ControllerBase
{
    private const int MaxUploadBytes = 5 * 1024 * 1024; // 5 MB

    private readonly IRecipeImportService _import;

    public AdminImportController(IRecipeImportService import)
    {
        _import = import;
    }

    /// <summary>Դատարկ template՝ լրացված օրինակով։</summary>
    [HttpGet("template")]
    public IActionResult DownloadTemplate() =>
        File(Encoding.UTF8.GetBytes(_import.GetTemplateCsv()), "text/csv", "gialora-recipe-template.csv");

    /// <summary>CSV-ի բովանդակությունը որպես JSON (client-ը ֆայլը ինքն է կարդում)։</summary>
    [HttpPost("recipes")]
    public async Task<ActionResult<RecipeImportResultDto>> ImportRecipes(
        [FromBody] RecipeImportRequestDto request)
    {
        if (User.GetUserId() is not { } adminId)
            return Unauthorized();

        if (Encoding.UTF8.GetByteCount(request.CsvContent) > MaxUploadBytes)
            return BadRequest(new ApiErrorDto { Message = "The file is too large (limit: 5 MB)." });

        var result = await _import.ImportAsync(request, adminId);

        // Մասնակի հաջողությունը ՉԻ 400 — շատ ռեցեպտ կարող է ներմուծվել,
        // մի քանիսը՝ ոչ։ Client-ը ցույց է տալիս errors/warnings-ը։
        return Ok(result);
    }

    /// <summary>Multipart տարբերակը՝ ուղիղ ֆայլի վերբեռնման համար։</summary>
    [HttpPost("recipes/file")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<RecipeImportResultDto>> ImportRecipeFile(
        IFormFile file,
        [FromQuery] bool publish = false,
        [FromQuery] bool overwrite = false,
        [FromQuery] char delimiter = ',')
    {
        if (User.GetUserId() is not { } adminId)
            return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest(new ApiErrorDto { Message = "No file was uploaded." });

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var content = await reader.ReadToEndAsync();

        var result = await _import.ImportAsync(new RecipeImportRequestDto
        {
            CsvContent = content,
            Delimiter = delimiter,
            PublishImmediately = publish,
            OverwriteExisting = overwrite
        }, adminId);

        return Ok(result);
    }
}
