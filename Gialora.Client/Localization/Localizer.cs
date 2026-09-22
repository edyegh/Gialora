// Gialora.Client/Localization/Localizer.cs
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

namespace Gialora.Client.Localization;

public record LanguageOption(string Code, string NativeName);

/// <summary>
/// UI-ի թարգմանությունները՝ wwwroot/i18n/{lang}.json-ից։
///
/// Ինչու ոչ .resx + IStringLocalizer. csproj-ում InvariantGlobalization=true է
/// (ICU-ի ~1.1 ՄԲ-ը browser չենք ուղարկում), իսկ satellite assembly-ները հենց
/// CultureInfo-ի վրա են հենվում։ Պարզ JSON բառարանը ոչ մի culture-ի կարիք
/// չունի, թարգմանիչը կարող է խմբագրել առանց Visual Studio-ի, և ամսաթվերի
/// ամիս/օր անունները նույն ֆայլից ենք վերցնում։
///
/// Singleton — TokenStore-ի նման, որ բոլոր scope-երում նույն լեզուն լինի։
/// </summary>
public class Localizer
{
    public const string DefaultLanguage = "en";
    private const string StorageKey = "gialora.lang";

    public static readonly IReadOnlyList<LanguageOption> Languages = new[]
    {
        new LanguageOption("en", "English"),
        new LanguageOption("hy", "Հայերեն"),
        new LanguageOption("el", "Ελληνικά")
    };

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new();

    private Dictionary<string, string> _fallback = new();
    private Dictionary<string, string> _current = new();

    public Localizer(IWebAssemblyHostEnvironment env, IJSRuntime js)
    {
        // Ոչ թե API-ի HttpClient-ը — բառարանները static ֆայլեր են հենց այս site-ից
        _http = new HttpClient { BaseAddress = new Uri(env.BaseAddress) };
        _js = js;
    }

    public string Language { get; private set; } = DefaultLanguage;

    /// <summary>Լեզուն փոխվեց — LocalizedComponentBase-ը լսում է սա և re-render անում։</summary>
    public event Action? Changed;

    /// <summary>Բանալին թարգմանում է; բացակայող բանալին վերադարձնում է ինքն իրեն, որ UI-ում աչքի ընկնի։</summary>
    public string this[string key] =>
        _current.TryGetValue(key, out var value) ? value
        : _fallback.TryGetValue(key, out var fallback) ? fallback
        : key;

    /// <summary>Նույնը, բայց string.Format-ի placeholder-ներով ({0}, {1}…)։</summary>
    public string this[string key, params object?[] args] => string.Format(this[key], args);

    /// <summary>Enum-ի արժեքի ցուցադրվող անունը ("Enum.MealType.Breakfast"); բանալի չկա՝ enum-ի անունն է։</summary>
    public string Enum<T>(T value) where T : struct, System.Enum
    {
        var key = $"Enum.{typeof(T).Name}.{value}";
        var text = this[key];
        return text == key ? value.ToString() : text;
    }

    // --- Ամսաթվեր ---
    // InvariantGlobalization-ի պատճառով ToString("MMMM")-ը միշտ անգլերեն է.
    // անունները վերցնում ենք բառարանից։

    /// <summary>18 Sep 2026</summary>
    public string ShortDate(DateTime d) => $"{d.Day} {this[$"Date.MonthShort.{d.Month}"]} {d.Year}";

    /// <summary>18 September 2026</summary>
    public string LongDate(DateTime d) => $"{d.Day} {this[$"Date.Month.{d.Month}"]} {d.Year}";

    /// <summary>18 Sep</summary>
    public string DayMonth(DateTime d) => $"{d.Day} {this[$"Date.MonthShort.{d.Month}"]}";

    /// <summary>Friday</summary>
    public string Weekday(DateTime d) => this[$"Date.Day.{(int)d.DayOfWeek}"];

    public string DayMonth(DateOnly d) => DayMonth(d.ToDateTime(TimeOnly.MinValue));
    public string Weekday(DateOnly d) => Weekday(d.ToDateTime(TimeOnly.MinValue));

    // --- Lifecycle ---

    /// <summary>Program.cs-ը կանչում է առաջին render-ից ԱՌԱՋ, որ էջը անգլերենով չթարթի։</summary>
    public async Task InitializeAsync()
    {
        _fallback = await LoadAsync(DefaultLanguage);

        string? saved = null;
        try
        {
            saved = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        }
        catch (JSException)
        {
            // Արգելափակված storage — մնում ենք լռելյայն լեզվի վրա
        }

        var language = IsSupported(saved) ? saved! : DefaultLanguage;
        await ApplyAsync(language);
    }

    public async Task SetLanguageAsync(string language)
    {
        if (!IsSupported(language) || language == Language)
            return;

        await ApplyAsync(language);

        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, language);
        }
        catch (JSException)
        {
            // Չպահվեց — լեզուն այս session-ում ամեն դեպքում փոխված է
        }

        Changed?.Invoke();
    }

    private async Task ApplyAsync(string language)
    {
        _current = language == DefaultLanguage ? _fallback : await LoadAsync(language);
        Language = language;

        try
        {
            // Screen reader-ների ու hyphenation-ի համար <html lang> պիտի համընկնի
            await _js.InvokeVoidAsync("eval", $"document.documentElement.lang='{language}'");
        }
        catch (JSException)
        {
            // Կոսմետիկ է — անտեսում ենք
        }
    }

    private async Task<Dictionary<string, string>> LoadAsync(string language)
    {
        if (_cache.TryGetValue(language, out var cached))
            return cached;

        Dictionary<string, string> dictionary;
        try
        {
            dictionary = await _http.GetFromJsonAsync<Dictionary<string, string>>($"i18n/{language}.json")
                         ?? new Dictionary<string, string>();
        }
        catch (Exception)
        {
            // Ֆայլը չկա/վնասված է — բանալիները կերևան, բայց հավելվածը չի կոտրվի
            dictionary = new Dictionary<string, string>();
        }

        _cache[language] = dictionary;
        return dictionary;
    }

    private static bool IsSupported(string? code) =>
        code is not null && Languages.Any(l => l.Code == code);
}
