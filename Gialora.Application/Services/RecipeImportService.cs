// Gialora.Application/Services/RecipeImportService.cs
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;

namespace Gialora.Application.Services;

public interface IRecipeImportService
{
    Task<RecipeImportResultDto> ImportAsync(RecipeImportRequestDto request, Guid adminId);

    /// <summary>Ներբեռնվող CSV template — admin-ը դրանով է լրացնում իր աղյուսակը։</summary>
    string GetTemplateCsv();
}

/// <summary>
/// Bulk recipe import (app structure §12՝ "automate your recipe uploading")։
///
/// Ձևաչափը՝ մեկ տող = մեկ բաղադրիչ։ Նույն "recipe" սյան արժեքով տողերը միավորվում են
/// մեկ ռեցեպտի մեջ, ուստի ռեցեպտի մետատվյալները կարելի է գրել միայն առաջին տողում։
/// </summary>
public class RecipeImportService : IRecipeImportService
{
    private readonly GialoraDbContext _db;
    private readonly ILogger<RecipeImportService> _logger;

    public RecipeImportService(GialoraDbContext db, ILogger<RecipeImportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public string GetTemplateCsv()
    {
        var builder = new StringBuilder();
        builder.AppendLine("recipe,description,mealtype,cuisine,difficulty,diettype,protein,servings,preptime,cooktime,minagemonths,freezer,lunchbox,kidfriendly,highprotein,ironrich,batch,image,tags,ingredient,quantity,unit,category,note,optional,instructions");
        builder.AppendLine("Chicken meatballs,Soft Mediterranean meatballs kids love,Dinner,Mediterranean,Easy,Meat,Chicken,4,15,20,12,yes,yes,yes,yes,no,yes,,\"kid-friendly|high-protein\",Chicken mince,500,Gram,Meat,,no,\"Mix everything|Shape into balls|Bake 20 min at 200C\"");
        builder.AppendLine("Chicken meatballs,,,,,,,,,,,,,,,,,,,Egg,1,Piece,DairyAndEggs,,no,");
        builder.AppendLine("Chicken meatballs,,,,,,,,,,,,,,,,,,,Onion,100,Gram,Vegetables,finely chopped,no,");
        builder.AppendLine("Chicken meatballs,,,,,,,,,,,,,,,,,,,Breadcrumbs,50,Gram,Pantry,,no,");
        return builder.ToString();
    }

    public async Task<RecipeImportResultDto> ImportAsync(RecipeImportRequestDto request, Guid adminId)
    {
        var result = new RecipeImportResultDto();

        var rows = CsvReader.Parse(request.CsvContent, request.Delimiter);
        if (rows.Count == 0)
        {
            result.Errors.Add("The file is empty or has no header row.");
            return result;
        }

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant().Replace(" ", "")).ToList();
        var columns = header
            .Select((name, index) => (name, index))
            .GroupBy(x => x.name)
            .ToDictionary(g => g.Key, g => g.First().index);

        if (!columns.ContainsKey("recipe"))
        {
            result.Errors.Add("Missing required column \"recipe\". Download the template to see the expected format.");
            return result;
        }

        // Խմբավորում ենք ըստ ռեցեպտի անվան (case-insensitive), հերթականությունը պահելով
        var groups = new List<(string Title, List<string[]> Rows)>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var title = Value(row, columns, "recipe").Trim();

            if (title.Length == 0)
            {
                if (row.All(string.IsNullOrWhiteSpace))
                    continue; // դատարկ տող — լուռ բաց ենք թողնում

                result.Warnings.Add($"Row {i + 1}: skipped because the \"recipe\" column is empty.");
                continue;
            }

            if (!index.TryGetValue(title, out var groupIndex))
            {
                groupIndex = groups.Count;
                index[title] = groupIndex;
                groups.Add((title, new List<string[]>()));
            }

            groups[groupIndex].Rows.Add(row);
        }

        if (groups.Count == 0)
        {
            result.Errors.Add("No recipe rows were found below the header.");
            return result;
        }

        // Բոլոր բաղադրիչներն ու tag-երը կարդում ենք ՄԵԿ անգամ, ոչ թե ամեն տողի համար
        var ingredientCache = await _db.Ingredients
            .ToDictionaryAsync(i => i.Name.ToLowerInvariant(), i => i);

        var tagCache = await _db.Tags
            .ToDictionaryAsync(t => t.Name, t => t);

        foreach (var (title, groupRows) in groups)
        {
            try
            {
                await ImportOneAsync(title, groupRows, columns, request, adminId, ingredientCache, tagCache, result);
            }
            catch (Exception ex)
            {
                // Մեկ վատ ռեցեպտը չպիտի տապալի ամբողջ import-ը
                result.Errors.Add($"\"{title}\": {ex.Message}");
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Recipe import finished: {Created} created, {Updated} updated, {Skipped} skipped, {Errors} errors",
            result.RecipesCreated, result.RecipesUpdated, result.RecipesSkipped, result.Errors.Count);

        return result;
    }

    private async Task ImportOneAsync(
        string title,
        List<string[]> rows,
        Dictionary<string, int> columns,
        RecipeImportRequestDto request,
        Guid adminId,
        Dictionary<string, Ingredient> ingredientCache,
        Dictionary<string, Tag> tagCache,
        RecipeImportResultDto result)
    {
        var existing = await _db.Recipes
            .Include(r => r.RecipeIngredients)
            .Include(r => r.RecipeTags)
            .FirstOrDefaultAsync(r => r.Title == title);

        if (existing is not null && !request.OverwriteExisting)
        {
            result.RecipesSkipped++;
            result.Warnings.Add($"\"{title}\": already exists — skipped (enable overwrite to update it).");
            return;
        }

        // Մետատվյալները վերցնում ենք առաջին ոչ-դատարկ արժեքից՝ ամբողջ խմբի ներսում.
        // դա թույլ է տալիս լրացնել դրանք միայն մեկ տողում։
        string Meta(string column) => rows
            .Select(r => Value(r, columns, column))
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

        var instructions = Meta("instructions").Replace("|", "\n");
        if (instructions.Length == 0)
            throw new ValidationFailedException("no instructions provided.");

        var recipe = existing ?? new Recipe();

        recipe.Title = title;
        recipe.Description = Nullify(Meta("description"));
        recipe.Instructions = instructions;
        recipe.ImageUrl = Nullify(Meta("image"));
        recipe.MealType = ParseEnum(Meta("mealtype"), MealType.Dinner);
        recipe.Cuisine = ParseEnum(Meta("cuisine"), CuisineType.Mediterranean);
        recipe.Difficulty = ParseEnum(Meta("difficulty"), DifficultyLevel.Easy);
        recipe.DietType = ParseEnum(Meta("diettype"), DietType.Meat);
        recipe.PrimaryProtein = ParseEnum(Meta("protein"), ProteinType.None);
        recipe.Servings = ParseInt(Meta("servings"), 4, 1, 50);
        recipe.PrepTimeMinutes = ParseInt(Meta("preptime"), 0, 0, 600);
        recipe.CookTimeMinutes = ParseInt(Meta("cooktime"), 0, 0, 600);
        recipe.MinAgeMonths = ParseInt(Meta("minagemonths"), 0, 0, 1200);
        recipe.IsFreezerFriendly = ParseBool(Meta("freezer"));
        recipe.IsLunchboxFriendly = ParseBool(Meta("lunchbox"));
        recipe.IsKidFriendly = ParseBool(Meta("kidfriendly"));
        recipe.IsHighProtein = ParseBool(Meta("highprotein"));
        recipe.IsIronRich = ParseBool(Meta("ironrich"));
        recipe.IsBatchFriendly = ParseBool(Meta("batch"));
        recipe.CreatedByAdminId = adminId;

        if (request.PublishImmediately && !recipe.IsPublished)
            recipe.PublishedAtUtc = DateTime.UtcNow;
        recipe.IsPublished = request.PublishImmediately;

        if (existing is null)
        {
            recipe.Slug = await Slug.UniqueAsync(title, s => _db.Recipes.AnyAsync(r => r.Slug == s));
            _db.Recipes.Add(recipe);
        }
        else
        {
            // Միայն RemoveRange — collection-ը ձեռք չենք տալիս (տես ներքևի ծանոթագրությունը)
            _db.RecipeIngredients.RemoveRange(recipe.RecipeIngredients);
            _db.RecipeTags.RemoveRange(recipe.RecipeTags);
        }

        // Նոր join-տողերը հավաքում ենք ԼՈԿԱԼ ցուցակում և ուղիղ AddRange անում։
        // Tracked recipe-ի navigation collection-ին ավելացնելը EF-ին ստիպում է
        // լրացված key-ով տողը համարել գոյություն ունեցող → UPDATE 0 տողի վրա։
        var newTags = new List<RecipeTag>();
        var newIngredients = new List<RecipeIngredient>();

        // --- Tags ---
        var tagNames = Meta("tags")
            .Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(RecipeService.Normalize)
            .Where(t => t.Length > 0)
            .Distinct();

        foreach (var tagName in tagNames)
        {
            if (!tagCache.TryGetValue(tagName, out var tag))
            {
                tag = new Tag { Name = tagName, DisplayName = RecipeService.ToDisplayName(tagName) };
                _db.Tags.Add(tag);
                tagCache[tagName] = tag;
                result.TagsCreated++;
            }

            newTags.Add(new RecipeTag { RecipeId = recipe.Id, Tag = tag });
        }

        // --- Ingredients ---
        var seenIngredients = new HashSet<Guid>();
        var sortOrder = 0;

        foreach (var row in rows)
        {
            var ingredientName = Value(row, columns, "ingredient").Trim();
            if (ingredientName.Length == 0)
                continue;

            var key = ingredientName.ToLowerInvariant();

            if (!ingredientCache.TryGetValue(key, out var ingredient))
            {
                if (!request.CreateMissingIngredients)
                {
                    result.Warnings.Add($"\"{title}\": ingredient \"{ingredientName}\" is unknown — skipped.");
                    continue;
                }

                ingredient = new Ingredient
                {
                    Name = ingredientName,
                    Category = ParseEnum(Value(row, columns, "category"), IngredientCategory.Other),
                    DefaultUnit = ParseEnum(Value(row, columns, "unit"), UnitOfMeasure.Gram)
                };

                _db.Ingredients.Add(ingredient);
                ingredientCache[key] = ingredient;
                result.IngredientsCreated++;
            }

            // Նույն բաղադրիչը երկու տողում → composite key-ի բախում։ Առաջինը պահում ենք։
            if (ingredient.Id != Guid.Empty && !seenIngredients.Add(ingredient.Id))
            {
                result.Warnings.Add($"\"{title}\": ingredient \"{ingredientName}\" listed more than once — kept the first row.");
                continue;
            }

            newIngredients.Add(new RecipeIngredient
            {
                RecipeId = recipe.Id,
                Ingredient = ingredient,
                Quantity = ParseDecimal(Value(row, columns, "quantity"), 1m),
                Unit = ParseEnum(Value(row, columns, "unit"), ingredient.DefaultUnit),
                Note = Nullify(Value(row, columns, "note")),
                IsOptional = ParseBool(Value(row, columns, "optional")),
                SortOrder = sortOrder++
            });
        }

        if (newIngredients.Count == 0)
            throw new ValidationFailedException("no ingredients were listed.");

        _db.RecipeTags.AddRange(newTags);
        _db.RecipeIngredients.AddRange(newIngredients);

        if (existing is null)
            result.RecipesCreated++;
        else
            result.RecipesUpdated++;
    }

    // -----------------------------------------------------------------------
    // Parsing helpers — ամեն մեկը "ներողամիտ" է, որ Excel-ի export-ը չտապալվի
    // -----------------------------------------------------------------------

    private static string Value(string[] row, Dictionary<string, int> columns, string column) =>
        columns.TryGetValue(column, out var i) && i < row.Length ? row[i] : string.Empty;

    private static string? Nullify(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value.Trim().Replace(" ", "").Replace("-", ""), ignoreCase: true, out var parsed)
            ? parsed
            : fallback;

    private static int ParseInt(string value, int fallback, int min, int max) =>
        int.TryParse(value.Trim(), out var parsed) ? Math.Clamp(parsed, min, max) : fallback;

    private static decimal ParseDecimal(string value, decimal fallback)
    {
        var text = value.Trim().Replace(',', '.');

        return decimal.TryParse(text,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static bool ParseBool(string value) =>
        value.Trim().ToLowerInvariant() is "yes" or "y" or "true" or "1" or "x";
}

/// <summary>
/// Փոքրիկ CSV parser՝ չակերտների ու ներդրված delimiter-ների աջակցությամբ։
/// Արտաքին package չենք ավելացնում մեկ ֆայլի ձևաչափի համար։
/// </summary>
internal static class CsvReader
{
    public static List<string[]> Parse(string content, char delimiter)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrWhiteSpace(content))
            return rows;

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        content = content.Replace("\r\n", "\n").Replace('\r', '\n');

        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    // "" ներսում = մեկ չակերտ
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                rows.Add(fields.ToArray());
                fields.Clear();
            }
            else
            {
                field.Append(ch);
            }
        }

        // Վերջին տողը՝ առանց փակող newline-ի
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add(fields.ToArray());
        }

        return rows;
    }
}
