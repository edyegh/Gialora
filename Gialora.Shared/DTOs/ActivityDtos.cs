// Gialora.Shared/DTOs/ActivityDtos.cs
using System.ComponentModel.DataAnnotations;
using Gialora.Shared.Enums;

namespace Gialora.Shared.Dtos;

/// <summary>
/// Մեկ գործողություն browser-ից։ Ժամանակը server-ն է դնում — client-ի ժամացույցին
/// չենք վստահում, իսկ batch-ի մի քանի վայրկյան ուշացումը վիճակագրության համար անկարևոր է։
/// </summary>
public class ActivityEventDto
{
    public ActivityType Type { get; set; }

    /// <summary>Click-ի դեպքում՝ կոճակի անունը (data-track, հղման path կամ տեքստ)։</summary>
    [StringLength(120)]
    public string? Target { get; set; }

    /// <summary>Էջը, որտեղ տեղի ունեցավ (server-ը GUID-ները և query string-ը հանում է)։</summary>
    [StringLength(300)]
    public string? Path { get; set; }

    /// <summary>Միայն RecipeView-ի համար։</summary>
    public Guid? RecipeId { get; set; }
}

public class ActivityBatchDto
{
    public const int MaxEvents = 50;

    [Required, MaxLength(MaxEvents)]
    public List<ActivityEventDto> Events { get; set; } = new();
}

/// <summary>Վիճակագրության ցուցակի տողը՝ "ինչ" + "քանի անգամ"։ Slug-ը՝ ռեցեպտի հղման համար։</summary>
public class StatCountDto
{
    public string Label { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public int Count { get; set; }
}

public class AdminUserStatsRowDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime? LastActiveUtc { get; set; }

    /// <summary>Ընտրված ժամանակահատվածի գործողությունները։</summary>
    public int Events { get; set; }

    /// <summary>Պահպանված ռեցեպտները՝ ամբողջ ժամանակի։</summary>
    public int Favorites { get; set; }
}

/// <summary>Admin-ի վիճակագրության գլխավոր էջը։</summary>
public class AdminStatsDto
{
    public int Days { get; set; }

    public int TotalUsers { get; set; }
    public int NewUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int PageViews { get; set; }
    public int Clicks { get; set; }
    public int RecipeViews { get; set; }
    public int FavoritesAdded { get; set; }
    public int PlansCreated { get; set; }

    public List<StatCountDto> MostSavedRecipes { get; set; } = new();
    public List<StatCountDto> MostLikedRecipes { get; set; } = new();
    public List<StatCountDto> MostViewedRecipes { get; set; } = new();
    public List<StatCountDto> TopClicks { get; set; } = new();
    public List<StatCountDto> TopPages { get; set; } = new();

    public List<AdminUserStatsRowDto> Users { get; set; } = new();
}

public class RecipeRefDto
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTime AtUtc { get; set; }
}

public class ActivityLogItemDto
{
    public ActivityType Type { get; set; }
    public string? Target { get; set; }
    public string? Path { get; set; }
    public string? RecipeTitle { get; set; }
    public string? RecipeSlug { get; set; }
    public DateTime AtUtc { get; set; }
}

/// <summary>Մեկ user-ի վիճակագրությունը՝ ինչ է հավանում, ինչ է սեղմում, ինչ է անում։</summary>
public class AdminUserActivityDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastActiveUtc { get; set; }
    public int Days { get; set; }

    public int PageViews { get; set; }
    public int Clicks { get; set; }
    public int RecipeViews { get; set; }

    public List<RecipeRefDto> SavedRecipes { get; set; } = new();
    public List<RecipeRefDto> LikedRecipes { get; set; } = new();
    public List<StatCountDto> MostViewedRecipes { get; set; } = new();
    public List<StatCountDto> TopClicks { get; set; } = new();
    public List<StatCountDto> TopPages { get; set; } = new();

    /// <summary>Վերջին գործողությունները, նորից՝ հին։</summary>
    public List<ActivityLogItemDto> Recent { get; set; } = new();
}
