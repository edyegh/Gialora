// Gialora.Api/Controllers/MetaController.cs
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;

namespace Gialora.Api.Controllers;

/// <summary>
/// Enum-ների ցուցակները՝ արժեք + ընթեռնելի անուն։
///
/// Blazor client-ը սա ՉԻ օգտագործում — նա reference է անում Gialora.Shared-ը և
/// enum-երը կարդում է ուղիղ, առանց ցանցի։ Այս endpoint-ը այլ լեզվով գրված
/// consumer-ների համար է (app structure §8՝ մեկ backend → website + mobile app),
/// որ նրանք ստիպված չլինեն enum-ների ցանկը ձեռքով պատճենել։
/// </summary>
[ApiController]
[Route("api/meta")]
[AllowAnonymous]
public partial class MetaController : ControllerBase
{
    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex CamelHumps();

    [HttpGet]
    public ActionResult<Dictionary<string, List<EnumOptionDto>>> GetAll() => Ok(new Dictionary<string, List<EnumOptionDto>>
    {
        ["mealTypes"] = Options<MealType>(),
        ["cuisines"] = Options<CuisineType>(),
        ["difficulties"] = Options<DifficultyLevel>(),
        ["dietTypes"] = Options<DietType>(),
        ["proteins"] = Options<ProteinType>(),
        ["ingredientCategories"] = Options<IngredientCategory>(),
        ["units"] = Options<UnitOfMeasure>(),
        ["planTypes"] = Options<MealPlanType>(),
        ["memberTypes"] = Options<FamilyMemberType>(),
        ["budgets"] = Options<BudgetLevel>()
    });

    private static List<EnumOptionDto> Options<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>()
            .Select(value => new EnumOptionDto
            {
                Value = Convert.ToInt32(value),
                Name = value.ToString()!,
                DisplayName = Humanize(value.ToString()!)
            })
            .ToList();

    /// <summary>"BatchAndFreeze" → "Batch and freeze"։</summary>
    private static string Humanize(string name)
    {
        var spaced = CamelHumps().Replace(name, " $1").Trim();
        var lowered = spaced.ToLowerInvariant()
            .Replace(" and ", " & ");

        return char.ToUpperInvariant(lowered[0]) + lowered[1..];
    }
}
