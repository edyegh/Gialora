// Gialora.Shared/DTOs/ImportDtos.cs
using System.ComponentModel.DataAnnotations;

namespace Gialora.Shared.Dtos;

/// <summary>
/// Bulk recipe import (app structure §12)։ Structured template-ը՝ մեկ տող = մեկ բաղադրիչ,
/// տողերը խմբավորվում են ըստ "Recipe name"-ի։
///
/// Սյուները (header-ը պարտադիր է, հերթականությունը՝ ազատ)՝
/// recipe | description | mealtype | cuisine | difficulty | diettype | protein |
/// servings | preptime | cooktime | minagemonths | freezer | lunchbox | kidfriendly |
/// highprotein | ironrich | batch | image | tags | ingredient | quantity | unit |
/// category | note | optional | instructions
/// </summary>
public class RecipeImportRequestDto
{
    [Required]
    public string CsvContent { get; set; } = string.Empty;

    /// <summary>Ինչ delimiter է օգտագործում ֆայլը (Excel-ի export-ը հաճախ ';' է)։</summary>
    public char Delimiter { get; set; } = ',';

    /// <summary>Բացակայող բաղադրիչները ինքնաբերաբար ստեղծե՞լ։</summary>
    public bool CreateMissingIngredients { get; set; } = true;

    /// <summary>Անմիջապես հրապարակե՞լ, թե՞ թողնել draft-ում admin-ի ստուգման համար։</summary>
    public bool PublishImmediately { get; set; } = false;

    /// <summary>Եթե նույն վերնագրով ռեցեպտ արդեն կա — թարմացնե՞լ այն, թե՞ բաց թողնել։</summary>
    public bool OverwriteExisting { get; set; } = false;
}

public class RecipeImportResultDto
{
    public int RecipesCreated { get; set; }
    public int RecipesUpdated { get; set; }
    public int RecipesSkipped { get; set; }
    public int IngredientsCreated { get; set; }
    public int TagsCreated { get; set; }

    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public bool Success => Errors.Count == 0;
}
