// Gialora.Shared/DTOs/CommonDtos.cs
namespace Gialora.Shared.Dtos;

/// <summary>
/// Էջավորված պատասխան։ Նախկինում list endpoint-ը վերադարձնում էր պարզ զանգված,
/// ուստի client-ը չգիտեր՝ կա՞ արդյոք հաջորդ էջը, և pagination չէր կարող կառուցել։
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

/// <summary>Enum-երը dropdown-ների համար — client-ը դրանք API-ից է վերցնում, չի կրկնօրինակում։</summary>
public class EnumOptionDto
{
    public int Value { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>Միասնական error payload — բոլոր 4xx/5xx-երը նույն ձևն ունեն։</summary>
public class ApiErrorDto
{
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string[]>? Errors { get; set; }
}
