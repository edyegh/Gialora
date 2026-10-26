// Gialora.Application/Services/ShoppingListService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;

namespace Gialora.Application.Services;

/// <summary>
/// "Smart shopping" — app structure §5-ի հիմնական խոստումը։
///
/// Երեք ռեցեպտի 500 գ + 750 գ + 450 գ հավի փոխարեն ցանկում լինում է մեկ տող՝
/// "Chicken mince — 1.7 kg", և կողքին գրված է, թե որ ուտեստների համար է։
/// </summary>
public class ShoppingListService : IShoppingListService
{
    private readonly GialoraDbContext _db;
    private readonly IFamilyService _familyService;
    private readonly IContentLanguage _language;
    private readonly ILogger<ShoppingListService> _logger;

    public ShoppingListService(
        GialoraDbContext db,
        IFamilyService familyService,
        IContentLanguage language,
        ILogger<ShoppingListService> logger)
    {
        _db = db;
        _familyService = familyService;
        _language = language;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Generate
    // -----------------------------------------------------------------------

    public async Task<ShoppingListDto> GenerateFromMealPlanAsync(Guid userId, Guid mealPlanId)
    {
        var familyId = await _familyService.GetFamilyIdAsync(userId);

        var plan = await _db.MealPlans
            .Include(p => p.ShoppingList)
                .ThenInclude(l => l!.Items)
            .Include(p => p.Days)
                .ThenInclude(d => d.Entries)
                    .ThenInclude(e => e.Recipe)
                        .ThenInclude(r => r.RecipeIngredients)
                            .ThenInclude(ri => ri.Ingredient)
                                .ThenInclude(i => i.Translations)
            .Include(p => p.Days)
                .ThenInclude(d => d.Entries)
                    .ThenInclude(e => e.Recipe)
                        .ThenInclude(r => r.Translations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == mealPlanId && p.FamilyId == familyId)
            ?? throw new NotFoundException("Meal plan not found.");

        // DisplayName/SourceRecipes-ը snapshot են (fallback). կարդալիս անունները
        // վերցվում են IngredientId/SourceRecipeIds-ով՝ ընթացիկ լեզվով (MapAsync)։
        var consolidated = Consolidate(plan, _language.Code);

        var list = plan.ShoppingList;
        var keptItemCount = 0;

        if (list is null)
        {
            list = new ShoppingList
            {
                FamilyId = familyId,
                MealPlanId = plan.Id,
                Name = $"Shopping — {plan.Name}",
                Status = ShoppingListStatus.Active
            };
            _db.ShoppingLists.Add(list);
        }
        else
        {
            // Regenerate — բայց ՊԱՀՊԱՆՈՒՄ ենք, թե ինչն է արդեն նշված։ Առանց սրա
            // շաբաթը վերագեներացնելը խանութում կջնջեր user-ի ամբողջ առաջընթացը։
            // IgnoreQueryFilters — "Clear checked"-ով հեռացված (soft-deleted) տողերն էլ են
            // գնված. առանց սրա regenerate-ը դրանք նորից չնշված կվերադարձներ։
            var alreadyChecked = (await _db.ShoppingListItems
                    .IgnoreQueryFilters()
                    .Where(i => i.ShoppingListId == list.Id && i.IsChecked && i.IngredientId != null)
                    .Select(i => i.IngredientId!.Value)
                    .ToListAsync())
                .ToHashSet();

            foreach (var item in consolidated.Where(c => c.IngredientId.HasValue))
            {
                if (alreadyChecked.Contains(item.IngredientId!.Value))
                    item.IsChecked = true;
            }

            // Ռեցեպտներից գեներացվածները derived data են — hard delete։
            // Ձեռքով ավելացրած տողերը (IngredientId == null) մնում են։
            var generated = list.Items.Where(i => i.IngredientId.HasValue).ToList();
            keptItemCount = list.Items.Count - generated.Count;

            // ՄԻԱՅՆ RemoveRange։ Նավիգացիոն collection-ը ձեռք չենք տալիս.
            // list.Items.Clear()-ը EF-ին ստիպում էր նույն տողերի համար և՛ DELETE,
            // և՛ orphan-ի UPDATE գեներացնել, ինչը ձախողվում էր
            // DbUpdateConcurrencyException-ով ("expected to affect 1 row, affected 0")։
            _db.ShoppingListItems.RemoveRange(generated);

            // Նախկինում "Clear checked"-ով soft-delete արված գեներացված տողերը նույնպես —
            // դրանց "գնված" լինելը արդեն alreadyChecked-ում է, իսկ տողերը կուտակվում էին
            _db.ShoppingListItems.RemoveRange(await _db.ShoppingListItems
                .IgnoreQueryFilters()
                .Where(i => i.ShoppingListId == list.Id && i.IsDeleted && i.IngredientId != null)
                .ToListAsync());
        }

        var sortOrder = keptItemCount;
        foreach (var item in consolidated)
        {
            item.SortOrder = sortOrder++;

            // FK-ը դնում ենք ուղիղ, ոչ թե collection-ի միջոցով։ Id-ն client-side է
            // գեներացվում (BaseEntity), ուստի սա աշխատում է նաև դեռ չպահված ցանկի համար։
            item.ShoppingListId = list.Id;
            _db.ShoppingListItems.Add(item);
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Կրկնակի click — զուգահեռ request-ը արդեն ստեղծեց (MealPlanId-ի unique index)
            // կամ վերագեներացրեց (նույն տողերի DELETE → 0 row)։ 500-ի փոխարեն՝ հաղթողի ցանկը։
            _db.ChangeTracker.Clear();
            return await GetForMealPlanAsync(userId, mealPlanId)
                ?? throw new NotFoundException("Meal plan not found.");
        }

        _logger.LogInformation(
            "Shopping list {ListId} generated from plan {PlanId} with {Count} consolidated items",
            list.Id, plan.Id, consolidated.Count);

        return (await GetByIdAsync(userId, list.Id))!;
    }

    /// <summary>
    /// Consolidation-ի սիրտը։ Երկու քայլ՝
    ///   1) ամեն ռեցեպտի քանակը scale ենք անում պլանավորված չափաբաժիններին,
    ///   2) նույն բաղադրիչի տողերը գումարում ենք ընդհանուր base միավորով։
    /// </summary>
    private static List<ShoppingListItem> Consolidate(MealPlan plan, string language)
    {
        // Բանալին ingredient + չափման "ընտանիք" է. 2 clove սխտոր և 5 գ սխտոր
        // չեն կարող գումարվել, ուստի դրանք առանձին տողեր են մնում։
        var buckets = new Dictionary<(Guid IngredientId, string UnitKey), Bucket>();

        var entries = plan.Days
            .Where(d => !d.IsDeleted)
            .SelectMany(d => d.Entries.Where(e => !e.IsDeleted));

        foreach (var entry in entries)
        {
            var recipe = entry.Recipe;
            if (recipe is null)
                continue;

            foreach (var ri in recipe.RecipeIngredients)
            {
                if (ri.Ingredient is null)
                    continue;

                // Ճշգրիտ scaling՝ ԱՌԱՆՑ կլորացման (UnitConverter.Scale-ը ամբողջական միավորները
                // վերև է կլորացնում)։ Կլորացնում ենք միայն գումարից հետո — այլապես երեք
                // ռեցեպտ × 0.5 բանկա = 3 բանկա, ոչ թե 2։
                var scaled = recipe.Servings > 0 && entry.PlannedServings > 0
                    ? ri.Quantity * entry.PlannedServings / recipe.Servings
                    : ri.Quantity;

                var key = (ri.IngredientId, UnitConverter.ConsolidationKey(ri.Unit));

                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new Bucket
                    {
                        IngredientId = ri.IngredientId,
                        Name = ri.Ingredient.NameIn(language),
                        Category = ri.Ingredient.Category,
                        ReferenceUnit = ri.Unit,
                        IsOptional = true
                    };
                    buckets[key] = bucket;
                }

                bucket.BaseQuantity += UnitConverter.ToBase(scaled, ri.Unit);
                bucket.Recipes[recipe.Id] = recipe.TitleIn(language);

                // Տողը ոչ-պարտադիր է միայն եթե ԲՈԼՈՐ ռեցեպտներում է ոչ-պարտադիր
                if (!ri.IsOptional)
                    bucket.IsOptional = false;
            }
        }

        return buckets.Values
            .Select(b =>
            {
                var (quantity, unit) = UnitConverter.FromBase(b.BaseQuantity, b.ReferenceUnit);

                // Ամբողջական միավորները կլորացնում ենք վերև — կես բանկա չես գնում
                if (UnitConverter.IsWholeUnit(unit))
                    quantity = Math.Ceiling(quantity);

                return new ShoppingListItem
                {
                    IngredientId = b.IngredientId,
                    DisplayName = b.Name,
                    Quantity = quantity,
                    Unit = unit,
                    Category = b.Category,
                    IsOptional = b.IsOptional,
                    SourceRecipes = b.Recipes.OrderBy(r => r.Value).Select(r => r.Value).ToList(),
                    SourceRecipeIds = b.Recipes.OrderBy(r => r.Value).Select(r => r.Key).ToList()
                };
            })
            .OrderBy(i => i.Category)
            .ThenBy(i => i.DisplayName)
            .ToList();
    }

    private class Bucket
    {
        public Guid IngredientId { get; init; }
        public string Name { get; init; } = string.Empty;
        public IngredientCategory Category { get; init; }
        public UnitOfMeasure ReferenceUnit { get; init; }
        public decimal BaseQuantity { get; set; }
        public bool IsOptional { get; set; }
        public Dictionary<Guid, string> Recipes { get; } = new();
    }

    // -----------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------

    public async Task<List<ShoppingListDto>> GetMyListsAsync(Guid userId)
    {
        var familyId = await _familyService.GetFamilyIdAsync(userId);

        var lists = await WithItems(_db.ShoppingLists.AsNoTracking())
            .Where(l => l.FamilyId == familyId && l.Status != ShoppingListStatus.Archived)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync();

        var titles = await SourceTitlesAsync(lists.SelectMany(l => l.Items));
        return lists.Select(l => Map(l, titles)).ToList();
    }

    public async Task<ShoppingListDto?> GetByIdAsync(Guid userId, Guid listId)
    {
        var list = await LoadAsync(userId, listId, tracking: false);
        return list is null ? null : await MapAsync(list);
    }

    public async Task<ShoppingListDto?> GetForMealPlanAsync(Guid userId, Guid mealPlanId)
    {
        var familyId = await _familyService.GetFamilyIdAsync(userId);

        var list = await WithItems(_db.ShoppingLists.AsNoTracking())
            .FirstOrDefaultAsync(l => l.MealPlanId == mealPlanId && l.FamilyId == familyId);

        return list is null ? null : await MapAsync(list);
    }

    // -----------------------------------------------------------------------
    // Mutations
    // -----------------------------------------------------------------------

    public async Task<ShoppingListItemDto?> SetItemCheckedAsync(Guid userId, Guid listId, Guid itemId, bool isChecked)
    {
        var list = await LoadAsync(userId, listId, tracking: true);
        if (list is null)
            return null;

        var item = list.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            return null;

        item.IsChecked = isChecked;
        await _db.SaveChangesAsync();
        return MapItem(item, await SourceTitlesAsync(new[] { item }));
    }

    public async Task<ShoppingListItemDto> AddItemAsync(Guid userId, Guid listId, ShoppingListItemCreateDto dto)
    {
        var list = await LoadAsync(userId, listId, tracking: true)
            ?? throw new NotFoundException("Shopping list not found.");

        var item = new ShoppingListItem
        {
            ShoppingListId = list.Id,
            DisplayName = dto.DisplayName.Trim(),
            Quantity = dto.Quantity,
            Unit = dto.Unit,
            Category = dto.Category,
            SortOrder = list.Items.Count
        };

        // Ուղիղ Add — ոչ թե list.Items.Add(item)։ Id-ն client-side է գեներացվում
        // (BaseEntity), իսկ tracked collection-ի մեջ ընկած՝ լրացված key-ով entity-ն
        // EF-ը համարում է ԱՐԴԵՆ ԳՈՅՈՒԹՅՈՒՆ ՈՒՆԵՑՈՂ և INSERT-ի փոխարեն UPDATE է գեներացնում,
        // որը 0 տող է փոխում և ձախողվում DbUpdateConcurrencyException-ով։
        _db.ShoppingListItems.Add(item);
        await _db.SaveChangesAsync();
        return MapItem(item, new Dictionary<Guid, string>());
    }

    public async Task<bool> RemoveItemAsync(Guid userId, Guid listId, Guid itemId)
    {
        var list = await LoadAsync(userId, listId, tracking: true);
        if (list is null)
            return false;

        var item = list.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            return false;

        item.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ShoppingListDto?> ClearCheckedAsync(Guid userId, Guid listId)
    {
        var list = await LoadAsync(userId, listId, tracking: true);
        if (list is null)
            return null;

        foreach (var item in list.Items.Where(i => i.IsChecked))
            item.IsDeleted = true;

        await _db.SaveChangesAsync();
        return await GetByIdAsync(userId, listId);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid listId)
    {
        var list = await LoadAsync(userId, listId, tracking: true);
        if (list is null)
            return false;

        list.IsDeleted = true;
        foreach (var item in list.Items)
            item.IsDeleted = true;

        await _db.SaveChangesAsync();
        return true;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<ShoppingList?> LoadAsync(Guid userId, Guid listId, bool tracking)
    {
        var familyId = await _familyService.GetFamilyIdAsync(userId);

        // Ingredient-ը պետք է IsPantryStaple-ի համար — առանց Include-ի ամեն տող
        // "ոչ staple" կերևար և "check your cupboard" խումբը միշտ դատարկ կմնար։
        var query = WithItems(tracking ? _db.ShoppingLists : _db.ShoppingLists.AsNoTracking());

        // FamilyId-ի ստուգումը — ուրիշի ցանկը չպիտի հասանելի լինի միայն Id-ով
        return await query.FirstOrDefaultAsync(l => l.Id == listId && l.FamilyId == familyId);
    }

    /// <summary>
    /// Տողեր + բաղադրիչ (IsPantryStaple-ի համար) + նրա թարգմանությունները (անունը
    /// ընթացիկ լեզվով)։ Split query — Items × Translations-ի դեկարտյան արտադրյալից խուսափելու համար։
    /// </summary>
    private static IQueryable<ShoppingList> WithItems(IQueryable<ShoppingList> query) =>
        query
            .Include(l => l.Items).ThenInclude(i => i.Ingredient).ThenInclude(i => i!.Translations)
            .AsSplitQuery();

    /// <summary>Տողերի "for: X, Y" ռեցեպտների վերնագրերը՝ ընթացիկ լեզվով, մեկ query-ով։</summary>
    private Task<Dictionary<Guid, string>> SourceTitlesAsync(IEnumerable<ShoppingListItem> items) =>
        _db.LoadRecipeTitlesAsync(items.SelectMany(i => i.SourceRecipeIds), _language.Code);

    private async Task<ShoppingListDto> MapAsync(ShoppingList list) =>
        Map(list, await SourceTitlesAsync(list.Items));

    private ShoppingListDto Map(ShoppingList list, IReadOnlyDictionary<Guid, string> recipeTitles) => new()
    {
        Id = list.Id,
        Name = list.Name,
        Status = list.Status,
        MealPlanId = list.MealPlanId,
        CreatedAtUtc = list.CreatedAtUtc,
        Groups = list.Items
            .Where(i => !i.IsDeleted)
            .GroupBy(i => i.Category)
            .OrderBy(g => g.Key)
            .Select(g => new ShoppingListGroupDto
            {
                Category = g.Key,
                CategoryName = CategoryName(g.Key),
                Items = g.Select(i => MapItem(i, recipeTitles))
                    .OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            })
            .ToList()
    };

    private ShoppingListItemDto MapItem(ShoppingListItem item, IReadOnlyDictionary<Guid, string> recipeTitles) => new()
    {
        Id = item.Id,
        IngredientId = item.IngredientId,
        // Կատալոգի բաղադրիչը՝ ընթացիկ լեզվով. ձեռքով ավելացրածը (կամ ջնջված բաղադրիչը)՝ ինչպես գրված է
        DisplayName = item.Ingredient?.Translations.Best(_language.Code)?.Name ?? item.DisplayName,
        Quantity = item.Quantity,
        Unit = item.Unit,
        QuantityDisplay = UnitConverter.Format(item.Quantity, item.Unit),
        Category = item.Category,
        IsChecked = item.IsChecked,
        IsOptional = item.IsOptional,
        // Ingredient-ի navigation-ը կարող է բեռնված չլինել (ձեռքով ավելացրած տող) —
        // այդ դեպքում staple չէ, և դա ճիշտ պատասխանն է։
        IsPantryStaple = item.Ingredient?.IsPantryStaple ?? false,
        // Հին տողերը SourceRecipeIds չունեն — դրանց համար մնում է պահված snapshot-ը
        SourceRecipes = item.SourceRecipeIds.Count == item.SourceRecipes.Count && item.SourceRecipeIds.Count > 0
            ? item.SourceRecipeIds
                .Select((id, index) => recipeTitles.GetValueOrDefault(id) ?? item.SourceRecipes[index])
                .ToList()
            : item.SourceRecipes
    };

    /// <summary>Խանութի բաժնի ընթեռնելի անունը — UI-ում խմբի վերնագիրն է։</summary>
    internal static string CategoryName(IngredientCategory category) => category switch
    {
        IngredientCategory.Vegetables => "Vegetables",
        IngredientCategory.Fruit => "Fruit",
        IngredientCategory.Meat => "Meat & poultry",
        IngredientCategory.Fish => "Fish & seafood",
        IngredientCategory.DairyAndEggs => "Dairy & eggs",
        IngredientCategory.Bakery => "Bakery",
        IngredientCategory.Grains => "Grains & pasta",
        IngredientCategory.Legumes => "Legumes",
        IngredientCategory.HerbsAndSpices => "Herbs & spices",
        IngredientCategory.OilsAndVinegars => "Oils & vinegars",
        IngredientCategory.Pantry => "Pantry",
        IngredientCategory.Frozen => "Frozen",
        IngredientCategory.Drinks => "Drinks",
        _ => "Other"
    };
}
