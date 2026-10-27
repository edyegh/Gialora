// Gialora.Application/Services/RecipeImportService.cs
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared;
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
        // Վերջին սյունակները ոչ պարտադիր թարգմանություններն են (_hy, _el)։ Ռեցեպտը
        // ճանաչվում է անգլերեն "recipe"-ով. թարգմանությունը գրվում է միայն եթե վերնագիրն
        // ու քայլերը երկուսն էլ կան, բաղադրիչի անունը՝ եթե այդ լեզվով դեռ անուն չունի։
        builder.AppendLine("recipe,description,mealtype,cuisine,difficulty,diettype,protein,servings,preptime,cooktime,minagemonths,freezer,lunchbox,kidfriendly,highprotein,ironrich,batch,image,tags,ingredient,quantity,unit,category,allergens,note,optional,instructions,recipe_hy,description_hy,instructions_hy,ingredient_hy,recipe_el,description_el,instructions_el,ingredient_el");
        builder.AppendLine("Chicken meatballs,Soft Mediterranean meatballs kids love,Dinner,Mediterranean,Easy,Meat,Chicken,4,15,20,12,yes,yes,yes,yes,no,yes,,\"kid-friendly|high-protein\",Chicken mince,500,Gram,Meat,none,,no,\"Mix everything|Shape into balls|Bake 20 min at 200C\",Հավի կոլոլակ,Փափուկ կոլոլակներ,\"Խառնել ամեն ինչ|Գնդիկներ ձևավորել|Թխել 20 րոպե 200°C-ում\",Հավի աղացած միս,,,,");
        builder.AppendLine("Chicken meatballs,,,,,,,,,,,,,,,,,,,Egg,1,Piece,DairyAndEggs,Eggs,,no,,,,,Ձու,,,,");
        builder.AppendLine("Chicken meatballs,,,,,,,,,,,,,,,,,,,Onion,100,Gram,Vegetables,none,finely chopped,no,,,,,Սոխ,,,,");
        builder.AppendLine("Chicken meatballs,,,,,,,,,,,,,,,,,,,Breadcrumbs,50,Gram,Pantry,Gluten,,no,,,,,Հացի փշրանք,,,,");
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

        // Բոլոր բաղադրիչներն ու tag-երը կարդում ենք ՄԵԿ անգամ, ոչ թե ամեն տողի համար։
        // CSV-ն անգլերեն է, բայց անունը համընկնում է ցանկացած լեզվի թարգմանության հետ
        // (անգլերենը՝ առաջնահերթ), որ հայերենով ստեղծված բաղադրիչը կրկնօրինակ չստանա։
        var ingredientCache = new Dictionary<string, Ingredient>();
        var knownNames = await _db.IngredientTranslations
            .Include(t => t.Ingredient)
            .OrderBy(t => t.Language == ContentLanguages.Default ? 0 : 1)
            .ToListAsync();
        foreach (var translation in knownNames)
            ingredientCache.TryAdd(translation.Name.ToLowerInvariant(), translation.Ingredient);

        var names = new ImportNames();
        foreach (var translation in knownNames)
            names.Add(translation.IngredientId, translation.Language, translation.Name);

        var tagCache = await _db.Tags
            .ToDictionaryAsync(t => t.Name, t => t);

        var reservedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (title, groupRows) in groups)
        {
            try
            {
                await ImportOneAsync(title, groupRows, columns, request, adminId, ingredientCache, names, tagCache, reservedSlugs, result);
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
        ImportNames names,
        Dictionary<string, Tag> tagCache,
        HashSet<string> reservedSlugs,
        RecipeImportResultDto result)
    {
        // Ռեցեպտը ճանաչում ենք ԱՆԳԼԵՐԵՆ վերնագրով (CSV-ի լեզուն)
        var existing = await _db.Recipes
            .Include(r => r.RecipeIngredients)
            .Include(r => r.RecipeTags)
            .Include(r => r.Translations)
            .FirstOrDefaultAsync(r => r.Translations.Any(t => t.Language == ContentLanguages.Default && t.Title == title));

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

        // Recipe-ին (և context-ին) ՉԵՆՔ դիպչում, մինչև բաղադրիչները չստուգենք. վերջում
        // մեկ SaveChanges է, և catch-ից հետո այն կպահեր կիսատ ռեցեպտ՝ առանց բաղադրիչների
        // (որը ալերգիայի filter-ը "անվտանգ" կհամարեր)։ Id-ն արդեն կա (BaseEntity)։
        var recipe = existing ?? new Recipe();

        // Նոր join-տողերը հավաքում ենք ԼՈԿԱԼ ցուցակում և ուղիղ AddRange անում։
        // Tracked recipe-ի navigation collection-ին ավելացնելը EF-ին ստիպում է
        // լրացված key-ով տողը համարել գոյություն ունեցող → UPDATE 0 տողի վրա։
        var newTags = new List<RecipeTag>();
        var newIngredients = new List<RecipeIngredient>();

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
                    Category = ParseEnum(Value(row, columns, "category"), IngredientCategory.Other),
                    DefaultUnit = ParseEnum(Value(row, columns, "unit"), UnitOfMeasure.Gram)
                };

                // "allergens" սյունակը՝ "TreeNuts|Milk" կամ "none"։ Դատարկ/սխալ արժեքի դեպքում
                // բաղադրիչը մնում է ՉՍՏՈՒԳՎԱԾ — ալերգիա ունեցող ընտանիքները այն չեն ստանա,
                // մինչև admin-ը չնշի (Admin → Ingredients → "Needs allergen review")։
                var allergenCell = Value(row, columns, "allergens");
                if (TryParseAllergens(allergenCell, out var allergens))
                {
                    ingredient.Allergens = allergens;
                    ingredient.AllergensReviewed = true;
                }
                else if (!string.IsNullOrWhiteSpace(allergenCell))
                {
                    result.Warnings.Add(
                        $"\"{title}\": ingredient \"{ingredientName}\" has unknown allergens \"{allergenCell}\" — " +
                        "it was created as needing allergen review.");
                }

                _db.Ingredients.Add(ingredient);
                _db.IngredientTranslations.Add(new IngredientTranslation
                {
                    IngredientId = ingredient.Id,
                    Language = ContentLanguages.Default,
                    Name = ingredientName
                });
                ingredientCache[key] = ingredient;
                names.Add(ingredient.Id, ContentLanguages.Default, ingredientName);
                result.IngredientsCreated++;
            }

            // ingredient_hy / ingredient_el — միայն եթե այդ լեզվով դեռ անուն չունի
            foreach (var language in TranslatedLanguages)
            {
                var translated = Value(row, columns, $"ingredient_{language}").Trim();
                if (translated.Length == 0 || names.Has(ingredient.Id, language))
                    continue;

                if (names.IsTaken(language, translated, ingredient.Id))
                {
                    result.Warnings.Add($"\"{title}\": {language} name \"{translated}\" already belongs to another ingredient — skipped.");
                    continue;
                }

                _db.IngredientTranslations.Add(new IngredientTranslation { IngredientId = ingredient.Id, Language = language, Name = translated });
                names.Add(ingredient.Id, language, translated);
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

        // Գրում ենք միայն անգլերեն թարգմանությունը — մյուս լեզուներինը overwrite-ից չեն տուժում
        var text = recipe.Translations.FirstOrDefault(t => t.Language == ContentLanguages.Default);
        if (text is null)
        {
            text = new RecipeTranslation { RecipeId = recipe.Id, Language = ContentLanguages.Default };
            _db.RecipeTranslations.Add(text);
        }
        text.Title = title;
        text.Description = Nullify(Meta("description"));
        text.Instructions = instructions;

        // recipe_hy/_el + instructions_hy/_el — թարգմանությունը գրվում է միայն ամբողջական
        // (առանց քայլերի հայերեն վերնագիրը էջում կթողներ դատարկ "Պատրաստում" բաժին)
        foreach (var language in TranslatedLanguages)
        {
            var translatedTitle = Meta($"recipe_{language}");
            if (translatedTitle.Length == 0)
                continue;

            var translatedSteps = Meta($"instructions_{language}").Replace("|", "\n");
            if (translatedSteps.Length == 0)
            {
                result.Warnings.Add($"\"{title}\": {language} title given without instructions_{language} — {language} translation skipped.");
                continue;
            }

            var translation = recipe.Translations.FirstOrDefault(t => t.Language == language);
            if (translation is null)
            {
                translation = new RecipeTranslation { RecipeId = recipe.Id, Language = language };
                _db.RecipeTranslations.Add(translation);
            }

            translation.Title = translatedTitle;
            translation.Description = Nullify(Meta($"description_{language}"));
            translation.Instructions = translatedSteps;
        }

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
            // reservedSlugs — նույն batch-ում արդեն տրված slug-երը (դեռ DB-ում չեն),
            // այլապես "Purée soup"/"Puree soup" նույն slug-ը կստանային → unique index → 500
            recipe.Slug = await Slug.UniqueAsync(title,
                async s => reservedSlugs.Contains(s) || await _db.Recipes.AnyAsync(r => r.Slug == s));
            reservedSlugs.Add(recipe.Slug);
            _db.Recipes.Add(recipe);
        }
        else
        {
            // Միայն RemoveRange — collection-ը ձեռք չենք տալիս (տես վերևի ծանոթագրությունը)
            _db.RecipeIngredients.RemoveRange(recipe.RecipeIngredients);
            _db.RecipeTags.RemoveRange(recipe.RecipeTags);
        }

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
                tag = new Tag { Name = tagName };
                _db.Tags.Add(tag);
                _db.TagTranslations.Add(new TagTranslation
                {
                    TagId = tag.Id,
                    Language = ContentLanguages.Default,
                    DisplayName = RecipeService.ToDisplayName(tagName)
                });
                tagCache[tagName] = tag;
                result.TagsCreated++;
            }

            newTags.Add(new RecipeTag { RecipeId = recipe.Id, Tag = tag });
        }

        _db.RecipeTags.AddRange(newTags);
        _db.RecipeIngredients.AddRange(newIngredients);

        if (existing is null)
            result.RecipesCreated++;
        else
            result.RecipesUpdated++;
    }

    /// <summary>Անգլերենից բացի լեզուները, որոնց սյունակները ("_hy", "_el") import-ը կարդում է։</summary>
    private static readonly string[] TranslatedLanguages =
        ContentLanguages.All.Where(l => l != ContentLanguages.Default).ToArray();

    /// <summary>
    /// Բաղադրիչների անունները ամեն լեզվով՝ import-ի ընթացքում։ Նոր ավելացվածներն էլ են
    /// այստեղ (DB-ում դեռ չկան), որ նույն ֆայլի երկու տողը նույն անունը երկու բաղադրիչի
    /// չտան — (Language, Name) unique index-ը կտապալեր ամբողջ SaveChanges-ը։
    /// </summary>
    private sealed class ImportNames
    {
        private readonly HashSet<(Guid, string)> _byIngredient = new();
        private readonly Dictionary<(string, string), Guid> _byName = new();

        public void Add(Guid ingredientId, string language, string name)
        {
            _byIngredient.Add((ingredientId, language));
            _byName.TryAdd((language, name.ToLowerInvariant()), ingredientId);
        }

        public bool Has(Guid ingredientId, string language) => _byIngredient.Contains((ingredientId, language));

        public bool IsTaken(string language, string name, Guid ingredientId) =>
            _byName.TryGetValue((language, name.ToLowerInvariant()), out var owner) && owner != ingredientId;
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

    /// <summary>
    /// "TreeNuts|Milk", "tree nuts | milk" կամ "none" (= ստուգված է, ալերգեն չկա)։
    /// Թվային արժեքները ("4") դիտավորյալ չեն ընդունվում, ինչպես և անհայտ անունները —
    /// կասկածելի արժեքը ավելի լավ է թողնել չստուգված, քան սխալ "անվտանգ" նշել։
    /// </summary>
    private static bool TryParseAllergens(string value, out List<Allergen> allergens)
    {
        allergens = new List<Allergen>();
        var cell = value.Trim();

        if (cell.Length == 0)
            return false;

        if (cell.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var token in cell.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = token.Replace(" ", "").Replace("-", "");
            if (name.All(char.IsDigit) || !Enum.TryParse<Allergen>(name, ignoreCase: true, out var allergen))
                return false;

            if (!allergens.Contains(allergen))
                allergens.Add(allergen);
        }

        return allergens.Count > 0;
    }

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
