// Gialora.Application/Services/MealPlanService.cs
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Application.Planning;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;

namespace Gialora.Application.Services;

/// <summary>
/// Կապում է engine-ը DB-ի հետ՝ թեկնածուներ է կառուցում, պլանը պահում,
/// swap/regenerate-ը կիրառում։ Ինքը կանոն չի պարունակում — կանոնները
/// <see cref="MealPlanningEngine"/>-ում են։
/// </summary>
public class MealPlanService : IMealPlanService
{
    private readonly GialoraDbContext _db;
    private readonly IMealPlanningEngine _engine;
    private readonly IFamilyService _familyService;
    private readonly IContentLanguage _language;
    private readonly ILogger<MealPlanService> _logger;

    public MealPlanService(
        GialoraDbContext db,
        IMealPlanningEngine engine,
        IFamilyService familyService,
        IContentLanguage language,
        ILogger<MealPlanService> logger)
    {
        _db = db;
        _engine = engine;
        _familyService = familyService;
        _language = language;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Generate
    // -----------------------------------------------------------------------

    public async Task<MealPlanDto> GenerateAsync(Guid userId, MealPlanGenerateDto dto)
    {
        var familyId = await _familyService.GetFamilyIdAsync(userId);
        var family = await LoadFamilyWithMembersAsync(familyId);

        var mealTypes = dto.MealTypes.Distinct().ToList();
        if (mealTypes.Count == 0)
            mealTypes.Add(MealType.Dinner);

        var daysCount = ResolveDayCount(dto, family);
        var servings = dto.ServingsPerMeal ?? FamilyPlanningInput.DefaultServings(family);
        var startDate = dto.WeekStartDate ?? DefaultStartDate(dto.PlanType);

        var candidates = await LoadCandidatesAsync(mealTypes, familyId);
        var constraints = FamilyPlanningInput.BuildConstraints(family);

        var result = _engine.Plan(candidates, new PlanningRequest
        {
            Constraints = constraints,
            SlotCount = daysCount * mealTypes.Count,
            // Slot-երը օր առ օր են՝ [Day1 Breakfast, Day1 Dinner, Day2 Breakfast, ...]
            SlotMealTypes = Enumerable.Range(0, daysCount).SelectMany(_ => mealTypes).ToList(),
            PlanType = dto.PlanType,
            OptimizeIngredientReuse = dto.OptimizeIngredientReuse,
            Seed = Random.Shared.Next()
        });

        if (result.Selection.Count == 0)
            throw new ValidationFailedException(result.Notes.FirstOrDefault()
                ?? PlanningNotes.Encode(PlanningNotes.NoMatches));

        var plan = new MealPlan
        {
            FamilyId = familyId,
            Name = BuildPlanName(dto.PlanType, startDate),
            PlanType = dto.PlanType,
            Status = MealPlanStatus.Draft,
            WeekStartDate = startDate,
            ServingsPerMeal = servings,
            PlanningNotes = result.Notes
        };

        // Engine-ը կարող է slot-եր դատարկ թողնել (օր.՝ նախաճաշի ռեցեպտները սպառվեցին),
        // ուստի ճաշերը տեղադրում ենք ըստ SlotIndexes-ի, ոչ թե հերթականությամբ
        var bySlot = result.SlotIndexes
            .Zip(result.Selection)
            .ToDictionary(x => x.First, x => x.Second);

        for (var dayIndex = 0; dayIndex < daysCount; dayIndex++)
        {
            var day = new MealPlanDay { Date = startDate.AddDays(dayIndex) };

            for (var mealIndex = 0; mealIndex < mealTypes.Count; mealIndex++)
            {
                if (!bySlot.TryGetValue(dayIndex * mealTypes.Count + mealIndex, out var candidate))
                    continue;

                day.Entries.Add(new MealPlanEntry
                {
                    MealType = mealTypes[mealIndex],
                    RecipeId = candidate.RecipeId,
                    PlannedServings = servings,
                    SortOrder = mealIndex
                });
            }

            if (day.Entries.Count > 0)
                plan.Days.Add(day);
        }

        _db.MealPlans.Add(plan);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Meal plan {PlanId} generated for family {FamilyId} ({Meals} meals)",
            plan.Id, familyId, result.Selection.Count);

        // Notes-ը արդեն պահված է plan-ի վրա, ուստի Map()-ը ինքն է դրանք վերադարձնում
        return (await GetByIdAsync(userId, plan.Id))!;
    }

    private static int ResolveDayCount(MealPlanGenerateDto dto, Family family)
    {
        if (dto.DaysCount is { } explicitCount)
            return Math.Clamp(explicitCount, 1, 14);

        return dto.PlanType switch
        {
            MealPlanType.Daily => 1,
            MealPlanType.Biweekly => Math.Clamp(family.CookingDaysPerWeek * 2, 1, 14),
            MealPlanType.BatchAndFreeze => 3, // մեկ խոհանոցային նստաշրջան = 3 ուտեստ
            _ => Math.Clamp(family.CookingDaysPerWeek, 1, 7)
        };
    }

    private static DateOnly DefaultStartDate(MealPlanType planType)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (planType is MealPlanType.Daily or MealPlanType.BatchAndFreeze)
            return today;

        // Շաբաթը սկսում ենք երկուշաբթիից — եթե այսօր երկուշաբթի է, այսօրվանից
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(daysUntilMonday);
    }

    private static string BuildPlanName(MealPlanType planType, DateOnly start)
    {
        // InvariantCulture-ը պարտադիր է. առանց դրա ամսվա անունը գալիս է սերվերի
        // locale-ից ("Week of 14 սեպ"), ինչը կախված է նրանից, թե որ մեքենայի վրա է
        // աշխատում API-ն — նույն պլանը տարբեր անուն կստանար տարբեր սերվերներում։
        var date = start.ToString("d MMM", CultureInfo.InvariantCulture);

        return planType switch
        {
            MealPlanType.Daily => $"Today — {date}",
            MealPlanType.BatchAndFreeze => $"Batch cook — {date}",
            MealPlanType.Biweekly => $"Two weeks from {date}",
            _ => $"Week of {date}"
        };
    }

    // -----------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------

    public const int MaxPlansListed = 52;

    public async Task<List<MealPlanDto>> GetMyPlansAsync(Guid userId)
    {
        var familyId = await _familyService.GetFamilyIdAsync(userId);

        // Սահման — առանց դրա ամեն կանչ բերում էր ընտանիքի ԲՈԼՈՐ պլանները (տարեկան 52+)՝
        // օրերով, ճաշերով, ռեցեպտներով ու tag-երով։ Ամենաթարմ MaxPlansListed-ը բավական է։
        var plans = await BaseQuery()
            .Where(p => p.FamilyId == familyId)
            .OrderByDescending(p => p.WeekStartDate)
            .ThenByDescending(p => p.CreatedAtUtc)
            .ThenBy(p => p.Id) // split query + Take-ը պահանջում է միանշանակ հերթականություն
            .Take(MaxPlansListed)
            .AsNoTracking()
            .ToListAsync();

        return plans.Select(Map).ToList();
    }

    public async Task<MealPlanDto?> GetByIdAsync(Guid userId, Guid planId)
    {
        var plan = await LoadPlanAsync(userId, planId, tracking: false);
        return plan is null ? null : Map(plan);
    }

    public async Task<MealPlanDto?> GetCurrentAsync(Guid userId)
    {
        var familyId = await _familyService.GetFamilyIdAsync(userId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Նախ՝ ընթացիկ/գալիք պլանը. եթե չկա — ամենավերջին անցյալինը
        var plan = await BaseQuery()
            .Where(p => p.FamilyId == familyId && p.Status != MealPlanStatus.Archived)
            .Where(p => p.Days.Any(d => d.Date >= today))
            .OrderBy(p => p.WeekStartDate)
            .FirstOrDefaultAsync()
            ?? await BaseQuery()
                .Where(p => p.FamilyId == familyId && p.Status != MealPlanStatus.Archived)
                .OrderByDescending(p => p.WeekStartDate)
                .FirstOrDefaultAsync();

        return plan is null ? null : Map(plan);
    }

    // -----------------------------------------------------------------------
    // Mutations
    // -----------------------------------------------------------------------

    public async Task<MealPlanDto?> SwapEntryAsync(Guid userId, Guid planId, Guid entryId, MealPlanSwapDto dto)
    {
        var plan = await LoadPlanAsync(userId, planId, tracking: true);
        if (plan is null)
            return null;

        var entry = plan.Days.SelectMany(d => d.Entries).FirstOrDefault(e => e.Id == entryId);
        if (entry is null)
            throw new NotFoundException("That meal is not part of this plan.");

        if (dto.RecipeId is { } explicitRecipeId)
        {
            // Ձեռքով ընտրված ռեցեպտն էլ է անցնում engine-ի կոշտ filter-ներով (ալերգիա,
            // դիետա, տարիք) և ճաշատեսակի ստուգումով — նախկինում ստուգվում էր միայն
            // "published"-ը, և ալերգիկ ընտանիքը կարող էր ստանալ վտանգավոր ուտեստ։
            var candidate = (await LoadCandidatesAsync(new[] { entry.MealType }, plan.FamilyId))
                .FirstOrDefault(c => c.RecipeId == explicitRecipeId);
            if (candidate is null)
                throw new ValidationFailedException($"That recipe does not exist, is not published, or is not a {entry.MealType} recipe.");

            var family = await LoadFamilyWithMembersAsync(plan.FamilyId);
            if (_engine.Filter(new[] { candidate }, FamilyPlanningInput.BuildConstraints(family)).Count == 0)
                throw new ValidationFailedException("That recipe doesn't fit your family's allergies, diet or ages.");

            entry.RecipeId = explicitRecipeId;
        }
        else
        {
            var replacement = await PickReplacementAsync(plan, entry);
            if (replacement is null)
                throw new ValidationFailedException(
                    "No other recipe matches your preferences for this slot. Try widening your filters.");

            entry.RecipeId = replacement.RecipeId;
        }

        // Ձեռքով փոխած ճաշը կողպում ենք — regenerate-ը այն չպիտի ջնջի
        entry.IsLocked = true;
        await _db.SaveChangesAsync();

        return await GetByIdAsync(userId, planId);
    }

    public async Task<MealPlanDto?> RegenerateDayAsync(Guid userId, Guid planId, Guid dayId)
    {
        var plan = await LoadPlanAsync(userId, planId, tracking: true);
        if (plan is null)
            return null;

        var day = plan.Days.FirstOrDefault(d => d.Id == dayId)
            ?? throw new NotFoundException("That day is not part of this plan.");

        await RefillAsync(plan, day.Entries.Where(e => !e.IsLocked).ToList());
        await _db.SaveChangesAsync();

        return await GetByIdAsync(userId, planId);
    }

    public async Task<MealPlanDto?> RegenerateAsync(Guid userId, Guid planId)
    {
        var plan = await LoadPlanAsync(userId, planId, tracking: true);
        if (plan is null)
            return null;

        var open = plan.Days.SelectMany(d => d.Entries).Where(e => !e.IsLocked).ToList();
        if (open.Count == 0)
            throw new ValidationFailedException("Every meal in this plan is locked. Unlock a meal to regenerate it.");

        await RefillAsync(plan, open);
        await _db.SaveChangesAsync();

        return await GetByIdAsync(userId, planId);
    }

    public async Task<MealPlanDto?> SetEntryLockedAsync(Guid userId, Guid planId, Guid entryId, bool isLocked)
    {
        var plan = await LoadPlanAsync(userId, planId, tracking: true);
        if (plan is null)
            return null;

        var entry = plan.Days.SelectMany(d => d.Entries).FirstOrDefault(e => e.Id == entryId)
            ?? throw new NotFoundException("That meal is not part of this plan.");

        entry.IsLocked = isLocked;
        await _db.SaveChangesAsync();

        return await GetByIdAsync(userId, planId);
    }

    public async Task<MealPlanDto?> AcceptAsync(Guid userId, Guid planId)
    {
        var plan = await LoadPlanAsync(userId, planId, tracking: true);
        if (plan is null)
            return null;

        plan.Status = MealPlanStatus.Accepted;
        await _db.SaveChangesAsync();

        return await GetByIdAsync(userId, planId);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid planId)
    {
        var plan = await LoadPlanAsync(userId, planId, tracking: true);
        if (plan is null)
            return false;

        plan.IsDeleted = true;
        foreach (var day in plan.Days)
        {
            day.IsDeleted = true;
            foreach (var entry in day.Entries)
                entry.IsDeleted = true;
        }

        // Առանց սրա ցանկը մնում էր "My lists"-ում՝ գոյություն չունեցող պլանի համար
        if (plan.ShoppingList is not null)
            plan.ShoppingList.IsDeleted = true;

        await _db.SaveChangesAsync();
        return true;
    }

    // -----------------------------------------------------------------------
    // Engine plumbing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Լրացնում է բաց slot-երը՝ հաշվի առնելով, թե ինչ է ԱՐԴԵՆ պլանում (locked ճաշերը)։
    /// Այդպես regenerate-ը չի կրկնում կողպված ուտեստը և չի կոտրում սպիտակուցի հերթափոխը։
    /// </summary>
    private async Task RefillAsync(MealPlan plan, List<MealPlanEntry> openEntries)
    {
        if (openEntries.Count == 0)
            return;

        var family = await LoadFamilyWithMembersAsync(plan.FamilyId);

        // Բոլոր ճաշատեսակները, ոչ միայն բացերինը — մնացող ճաշերի սպիտակուցները
        // (KeptMeals) պետք են, իսկ slot-երի տեսակները engine-ը ինքն է զտում
        var mealTypes = plan.Days.SelectMany(d => d.Entries).Select(e => e.MealType).Distinct().ToList();

        var candidates = await LoadCandidatesAsync(mealTypes, plan.FamilyId);

        // Պլանում ՄՆԱՑՈՂ բոլոր ճաշերը (կողպվածները և մյուս օրերի չկողպվածները) —
        // "regenerate day"-ը չպիտի կրկնի երկուշաբթիի ճաշը չորեքշաբթի օրը։
        var openIds = openEntries.Select(e => e.Id).ToHashSet();
        var keptRecipeIds = plan.Days
            .SelectMany(d => d.Entries)
            .Where(e => !openIds.Contains(e.Id))
            .Select(e => e.RecipeId)
            .ToHashSet();

        // Slot-երը լցնում ենք ամսաթվի հերթականությամբ, որ սպիտակուցի
        // "երկու օր անընդմեջ" կանոնը իմաստ ունենա
        var ordered = openEntries
            .OrderBy(e => plan.Days.First(d => d.Id == e.MealPlanDayId).Date)
            .ThenBy(e => e.SortOrder)
            .ToList();

        var result = _engine.Plan(candidates, new PlanningRequest
        {
            Constraints = FamilyPlanningInput.BuildConstraints(family),
            SlotCount = ordered.Count,
            SlotMealTypes = ordered.Select(e => e.MealType).ToList(),
            PlanType = plan.PlanType,
            OptimizeIngredientReuse = true,
            AlreadyUsedRecipeIds = keptRecipeIds,
            KeptMeals = candidates.Where(c => keptRecipeIds.Contains(c.RecipeId)).ToList(),
            Seed = Random.Shared.Next()
        });

        if (result.Selection.Count == 0)
            throw new ValidationFailedException(result.Notes.FirstOrDefault()
                ?? PlanningNotes.Encode(PlanningNotes.NoMatches));

        // Չլցված slot-ը պահում է իր հին ռեցեպտը
        for (var i = 0; i < result.Selection.Count; i++)
            ordered[result.SlotIndexes[i]].RecipeId = result.Selection[i].RecipeId;

        // Regenerate-ից հետո բացատրությունը պիտի նկարագրի ՆՈՐ պլանը, ոչ թե հինը
        plan.PlanningNotes = result.Notes;
    }

    private async Task<PlanningCandidate?> PickReplacementAsync(MealPlan plan, MealPlanEntry entry)
    {
        var family = await LoadFamilyWithMembersAsync(plan.FamilyId);
        var candidates = await LoadCandidatesAsync(new[] { entry.MealType }, plan.FamilyId);

        var currentRecipeIds = plan.Days
            .SelectMany(d => d.Entries)
            .Select(e => e.RecipeId)
            .ToHashSet();

        var currentPlan = candidates.Where(c => currentRecipeIds.Contains(c.RecipeId)).ToList();

        return _engine.PickReplacement(
            candidates,
            FamilyPlanningInput.BuildConstraints(family),
            currentPlan,
            entry.RecipeId,
            Random.Shared.Next());
    }

    /// <summary>
    /// Բոլոր հրապարակված ռեցեպտները՝ engine-ի ձևաչափով, ընտանիքի feedback-ի
    /// հիման վրա հաշվված history score-ով ("Prioritize recipes the family previously liked")։
    /// </summary>
    private async Task<List<PlanningCandidate>> LoadCandidatesAsync(
        IEnumerable<MealType> mealTypes, Guid familyId)
    {
        var types = mealTypes.Distinct().ToList();

        var raw = await _db.Recipes.AsNoTracking()
            .Where(r => r.IsPublished && types.Contains(r.MealType))
            .Select(r => new
            {
                r.Id,
                Titles = r.Translations.Select(t => new { t.Language, t.Title }).ToList(),
                r.MealType,
                r.DietType,
                r.PrimaryProtein,
                r.Cuisine,
                r.Difficulty,
                r.PrepTimeMinutes,
                r.CookTimeMinutes,
                r.MinAgeMonths,
                r.IsFreezerFriendly,
                r.IsBatchFriendly,
                r.IsKidFriendly,
                r.IsIronRich,
                r.IsHighProtein,
                r.EstimatedCostPerServing,
                Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
                Ingredients = r.RecipeIngredients.Select(ri => new
                {
                    ri.IngredientId,
                    // ԲՈԼՈՐ լեզուների անունները — "չսիրած" բառը ("սխտոր" կամ "garlic")
                    // պիտի համընկնի՝ անկախ նրանից, թե ընտանիքը որ լեզվով է այն գրել
                    Names = ri.Ingredient.Translations.Select(t => t.Name).ToList(),
                    ri.Ingredient.Category,
                    ri.Ingredient.Allergens,
                    ri.Ingredient.AllergensReviewed
                }).ToList()
            })
            .ToListAsync();

        var history = await LoadHistoryScoresAsync(familyId);

        return raw.Select(r => new PlanningCandidate
        {
            RecipeId = r.Id,
            Title = r.Titles.Best(_language.Code, t => t.Language)?.Title ?? string.Empty,
            MealType = r.MealType,
            DietType = r.DietType,
            PrimaryProtein = r.PrimaryProtein,
            Cuisine = r.Cuisine,
            Difficulty = r.Difficulty,
            TotalMinutes = r.PrepTimeMinutes + r.CookTimeMinutes,
            MinAgeMonths = r.MinAgeMonths,
            IsFreezerFriendly = r.IsFreezerFriendly,
            IsBatchFriendly = r.IsBatchFriendly,
            IsKidFriendly = r.IsKidFriendly,
            IsIronRich = r.IsIronRich,
            IsHighProtein = r.IsHighProtein,
            CostPerServing = r.EstimatedCostPerServing,
            IngredientIds = r.Ingredients.Select(i => i.IngredientId).ToHashSet(),
            IngredientNames = r.Ingredients.SelectMany(i => i.Names).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Tags = r.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase),
            VegetableCount = r.Ingredients.Count(i =>
                i.Category is IngredientCategory.Vegetables or IngredientCategory.Fruit),
            // Ռեցեպտը պարունակում է իր ցանկացած բաղադրիչի ալերգենը
            Allergens = r.Ingredients.SelectMany(i => i.Allergens).ToHashSet(),
            HasUnreviewedIngredients = r.Ingredients.Any(i => !i.AllergensReviewed),
            HistoryScore = history.GetValueOrDefault(r.Id)
        }).ToList();
    }

    /// <summary>
    /// "The system learns those preferences and improves subsequent recommendations"
    /// (app structure §1)։ Առայժմ սա պարզ կշիռների գումար է, ոչ ML — և հենց դա է
    /// փաստաթղթի առաջարկը MVP-ի համար։
    /// </summary>
    private async Task<Dictionary<Guid, double>> LoadHistoryScoresAsync(Guid familyId)
    {
        var userIds = await _db.Users
            .Where(u => u.FamilyId == familyId)
            .Select(u => u.Id)
            .ToListAsync();

        if (userIds.Count == 0)
            return new Dictionary<Guid, double>();

        var feedback = await _db.Feedbacks.AsNoTracking()
            .Where(f => userIds.Contains(f.UserId))
            .Select(f => new
            {
                f.RecipeId,
                f.Rating,
                f.Reaction,
                f.TooDifficult,
                f.KidsDidNotEat,
                f.TookTooLong
            })
            .ToListAsync();

        var favorites = await _db.FavoriteRecipes.AsNoTracking()
            .Where(f => userIds.Contains(f.UserId))
            .Select(f => f.RecipeId)
            .ToListAsync();

        var scores = new Dictionary<Guid, double>();

        void Add(Guid recipeId, double delta) =>
            scores[recipeId] = scores.GetValueOrDefault(recipeId) + delta;

        foreach (var f in feedback)
        {
            if (f.Reaction == FeedbackReaction.Liked) Add(f.RecipeId, 1.5);
            if (f.Reaction == FeedbackReaction.Disliked) Add(f.RecipeId, -2.5);

            if (f.Rating >= 4) Add(f.RecipeId, 1.0);
            else if (f.Rating is > 0 and <= 2) Add(f.RecipeId, -1.5);

            // "kids didn't eat it" — ամենաուժեղ բացասական ազդանշանը երեխաներով ընտանիքում
            if (f.KidsDidNotEat) Add(f.RecipeId, -2.0);
            if (f.TooDifficult) Add(f.RecipeId, -1.0);
            if (f.TookTooLong) Add(f.RecipeId, -0.75);
        }

        foreach (var recipeId in favorites)
            Add(recipeId, 1.0);

        return scores;
    }

    // -----------------------------------------------------------------------
    // Loading / mapping
    // -----------------------------------------------------------------------

    private IQueryable<MealPlan> BaseQuery() =>
        _db.MealPlans
            .AsNoTracking()
            // Երեք ներդրված collection (Days → Entries → RecipeTags) մեկ SQL-ում
            // տալիս են դեկարտյան արտադրյալ. 5 օր × 1 ճաշ × 6 tag = 30 տող մեկ պլանի
            // փոխարեն, և EF-ը դրանք հետո deduplicate է անում հիշողության մեջ։
            // Split query-ն ամեն collection-ը բերում է առանձին — շատ ավելի քիչ տվյալ։
            .AsSplitQuery()
            .Include(p => p.ShoppingList)
            .Include(p => p.Days.OrderBy(d => d.Date))
                .ThenInclude(d => d.Entries.OrderBy(e => e.SortOrder))
                    .ThenInclude(e => e.Recipe)
                        .ThenInclude(r => r.RecipeTags)
                            .ThenInclude(rt => rt.Tag)
            .Include(p => p.Days)
                .ThenInclude(d => d.Entries)
                    .ThenInclude(e => e.Recipe)
                        .ThenInclude(r => r.Translations);

    private async Task<MealPlan?> LoadPlanAsync(Guid userId, Guid planId, bool tracking)
    {
        var familyId = await _familyService.GetFamilyIdAsync(userId);

        var query = tracking
            ? _db.MealPlans
                .Include(p => p.ShoppingList)
                .Include(p => p.Days)
                    .ThenInclude(d => d.Entries)
                        .ThenInclude(e => e.Recipe)
                            .ThenInclude(r => r.RecipeTags)
                                .ThenInclude(rt => rt.Tag)
            : BaseQuery();

        // FamilyId-ի ստուգումը պարտադիր է — առանց դրա ցանկացած logged-in user
        // կկարողանար ուրիշի պլանը կարդալ/փոխել միայն Id-ն իմանալով։
        return await query.FirstOrDefaultAsync(p => p.Id == planId && p.FamilyId == familyId);
    }

    private async Task<Family> LoadFamilyWithMembersAsync(Guid familyId) =>
        await _db.Families
            .AsNoTracking()
            .Include(f => f.FamilyMembers)
            .FirstOrDefaultAsync(f => f.Id == familyId)
        ?? throw new NotFoundException("Family not found.");

    private MealPlanDto Map(MealPlan plan) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        PlanType = plan.PlanType,
        Status = plan.Status,
        WeekStartDate = plan.WeekStartDate,
        PlanningNotes = plan.PlanningNotes,
        ServingsPerMeal = plan.ServingsPerMeal,
        ShoppingListId = plan.ShoppingList?.Id,
        Days = plan.Days
            .Where(d => !d.IsDeleted)
            .OrderBy(d => d.Date)
            .Select(d => new MealPlanDayDto
            {
                Id = d.Id,
                Date = d.Date,
                Entries = d.Entries
                    .Where(e => !e.IsDeleted)
                    .OrderBy(e => e.SortOrder)
                    .Select(e => new MealPlanEntryDto
                    {
                        Id = e.Id,
                        MealType = e.MealType,
                        PlannedServings = e.PlannedServings,
                        IsLocked = e.IsLocked,
                        Recipe = MapRecipe(e.Recipe, _language.Code)
                    })
                    .ToList()
            })
            .ToList()
    };

    private static RecipeSummaryDto MapRecipe(Recipe r, string language)
    {
        var text = r.Translations.Best(language);

        return new RecipeSummaryDto
        {
            Id = r.Id,
            Title = text?.Title ?? string.Empty,
            Description = text?.Description,
            ContentLanguage = text?.Language ?? ContentLanguages.Default,
            Slug = r.Slug,
            ImageUrl = r.ImageUrl,
            MealType = r.MealType,
            Cuisine = r.Cuisine,
            Difficulty = r.Difficulty,
            DietType = r.DietType,
            PrimaryProtein = r.PrimaryProtein,
            PrepTimeMinutes = r.PrepTimeMinutes,
            CookTimeMinutes = r.CookTimeMinutes,
            Servings = r.Servings,
            MinAgeMonths = r.MinAgeMonths,
            IsFreezerFriendly = r.IsFreezerFriendly,
            IsLunchboxFriendly = r.IsLunchboxFriendly,
            IsKidFriendly = r.IsKidFriendly,
            IsHighProtein = r.IsHighProtein,
            IsIronRich = r.IsIronRich,
            IsBatchFriendly = r.IsBatchFriendly,
            IsPublished = r.IsPublished,
            Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList()
        };
    }
}
