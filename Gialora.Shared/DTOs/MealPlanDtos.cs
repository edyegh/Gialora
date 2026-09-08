// Gialora.Shared/DTOs/MealPlanDtos.cs
using System.ComponentModel.DataAnnotations;
using Gialora.Shared.Enums;

namespace Gialora.Shared.Dtos;

public class MealPlanDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MealPlanType PlanType { get; set; }
    public MealPlanStatus Status { get; set; }
    public DateOnly WeekStartDate { get; set; }
    public int ServingsPerMeal { get; set; }
    public Guid? ShoppingListId { get; set; }
    public List<MealPlanDayDto> Days { get; set; } = new();

    /// <summary>Ինչ կանոններ են կիրառվել — UI-ում բացատրում ենք, թե ինչու է այս պլանը։</summary>
    public List<string> PlanningNotes { get; set; } = new();
}

public class MealPlanDayDto
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public List<MealPlanEntryDto> Entries { get; set; } = new();
}

public class MealPlanEntryDto
{
    public Guid Id { get; set; }
    public MealType MealType { get; set; }
    public int PlannedServings { get; set; }
    public bool IsLocked { get; set; }
    public RecipeSummaryDto Recipe { get; set; } = new();
}

/// <summary>Ի՞նչ ենք ուղարկում "Generate my week" կոճակի հետ։</summary>
public class MealPlanGenerateDto
{
    public MealPlanType PlanType { get; set; } = MealPlanType.Weekly;

    /// <summary>Շաբաթվա սկիզբը։ null = այս գալիք երկուշաբթի։</summary>
    public DateOnly? WeekStartDate { get; set; }

    /// <summary>null = վերցնում ենք Family.CookingDaysPerWeek-ը։</summary>
    [Range(1, 14)]
    public int? DaysCount { get; set; }

    /// <summary>Որ ճաշատեսակներն ենք պլանավորում (default՝ միայն ընթրիք)։</summary>
    public List<MealType> MealTypes { get; set; } = new() { Enums.MealType.Dinner };

    [Range(1, 20)]
    public int? ServingsPerMeal { get; set; }

    /// <summary>Կիրառե՞լ "որքան հնարավոր է քիչ տարբեր բաղադրիչ" օպտիմիզացիան (app structure §5)։</summary>
    public bool OptimizeIngredientReuse { get; set; } = true;
}

public class MealPlanSwapDto
{
    /// <summary>Կոնկրետ ռեցեպտ, կամ null՝ եթե ուզում ենք, որ engine-ը ինքը ընտրի։</summary>
    public Guid? RecipeId { get; set; }
}
