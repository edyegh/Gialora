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
    private readonly ILogger<ShoppingListService> _logger;

    public ShoppingListService(
        GialoraDbContext db,
        IFamilyService familyService,
        ILogger<ShoppingListService> logger)
    {
        _db = db;
        _familyService = familyService;
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
            .FirstOrDefaultAsync(p => p.Id == mealPlanId && p.FamilyId == familyId)
            ?? throw new NotFoundException("Meal plan not found.");

        var consolidated = Consolidate(plan);

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
            var alreadyChecked = list.Items
                .Where(i => i.IsChecked && i.IngredientId.HasValue)
                .Select(i => i.IngredientId!.Value)
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

        await _db.SaveChangesAsync();

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
    private static List<ShoppingListItem> Consolidate(MealPlan plan)
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

                var scaled = UnitConverter.Scale(
                    ri.Quantity, ri.Unit,
                    fromServings: recipe.Servings,
                    toServings: entry.PlannedServings);

                var key = (ri.IngredientId, UnitConverter.ConsolidationKey(ri.Unit));

                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new Bucket
                    {
                        IngredientId = ri.IngredientId,
                        Name = ri.Ingredient.Name,
                        Category = ri.Ingredient.Category,
                        ReferenceUnit = ri.Unit,
                        IsOptional = true
                    };
                    buckets[key] = bucket;
                }

                bucket.BaseQuantity += UnitConverter.ToBase(scaled, ri.Unit);
                bucket.Recipes.Add(recipe.Title);

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
                    SourceRecipes = b.Recipes.OrderBy(r => r).ToList()
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
        public HashSet<string> Recipes { get; } = new();
    }

    // -----------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------

    public async Task<List<ShoppingListDto>> GetMyListsAsync(Guid userId)
    {
        var familyId = await _familyService.GetFamilyIdAsync(userId);

        var lists = await _db.ShoppingLists.AsNoTracking()
            .Include(l => l.Items).ThenInclude(i => i.Ingredient)
            .Where(l => l.FamilyId == familyId && l.Status != ShoppingListStatus.Archived)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync();

        return lists.Select(Map).ToList();
    }

    public async Task<ShoppingListDto?> GetByIdAsync(Guid userId, Guid listId)
    {
        var list = await LoadAsync(userId, listId, tracking: false);
        return list is null ? null : Map(list);
    }

    public async Task<ShoppingListDto?> GetForMealPlanAsync(Guid userId, Guid mealPlanId)
    {
        var familyId = await _familyService.GetFamilyIdAsync(userId);

        var list = await _db.ShoppingLists.AsNoTracking()
            .Include(l => l.Items).ThenInclude(i => i.Ingredient)
            .FirstOrDefaultAsync(l => l.MealPlanId == mealPlanId && l.FamilyId == familyId);

        return list is null ? null : Map(list);
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
        return MapItem(item);
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
        return MapItem(item);
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
        var query = tracking
            ? _db.ShoppingLists.Include(l => l.Items).ThenInclude(i => i.Ingredient)
            : _db.ShoppingLists.AsNoTracking().Include(l => l.Items).ThenInclude(i => i.Ingredient);

        // FamilyId-ի ստուգումը — ուրիշի ցանկը չպիտի հասանելի լինի միայն Id-ով
        return await query.FirstOrDefaultAsync(l => l.Id == listId && l.FamilyId == familyId);
    }

    private static ShoppingListDto Map(ShoppingList list) => new()
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
                Items = g.OrderBy(i => i.DisplayName).Select(MapItem).ToList()
            })
            .ToList()
    };

    private static ShoppingListItemDto MapItem(ShoppingListItem item) => new()
    {
        Id = item.Id,
        IngredientId = item.IngredientId,
        DisplayName = item.DisplayName,
        Quantity = item.Quantity,
        Unit = item.Unit,
        QuantityDisplay = UnitConverter.Format(item.Quantity, item.Unit),
        Category = item.Category,
        IsChecked = item.IsChecked,
        IsOptional = item.IsOptional,
        // Ingredient-ի navigation-ը կարող է բեռնված չլինել (ձեռքով ավելացրած տող) —
        // այդ դեպքում staple չէ, և դա ճիշտ պատասխանն է։
        IsPantryStaple = item.Ingredient?.IsPantryStaple ?? false,
        SourceRecipes = item.SourceRecipes
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
