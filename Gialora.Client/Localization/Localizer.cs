// Gialora.Client/Localization/Localizer.cs
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

namespace Gialora.Client.Localization;

public record LanguageOption(string Code, string NativeName);

/// <summary>
/// UI-ի թարգմանությունները՝ wwwroot/i18n/{lang}.json-ից, որոնց ՎՐԱ դրվում են
/// admin-ի խմբագրած տեքստերը (GET api/site-texts/{lang}, "Site texts" admin էջ)։
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
    private readonly HttpClient _api;
    private readonly IJSRuntime _js;
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new();
    private readonly Dictionary<string, Dictionary<string, string>> _siteTextCache = new();

    private Dictionary<string, string> _fallback = new();
    private Dictionary<string, string> _current = new();

    // Admin-ի override-ները. փնտրման կարգը՝ override(ընթացիկ) → JSON(ընթացիկ) →
    // override(en) → JSON(en) → բանալին ինքը
    private Dictionary<string, string> _fallbackSiteTexts = new();
    private Dictionary<string, string> _currentSiteTexts = new();

    public Localizer(IWebAssemblyHostEnvironment env, IJSRuntime js, Uri apiBaseAddress)
    {
        // Բառարանները static ֆայլեր են հենց այս site-ից
        _http = new HttpClient { BaseAddress = new Uri(env.BaseAddress) };

        // Override-ները API-ից են, առանց auth-ի (հանրային GET)։ Կարճ timeout — API-ն
        // անջատված լինելու դեպքում առաջին render-ը չպիտի կախվի, JSON-ը բավական է
        _api = new HttpClient { BaseAddress = apiBaseAddress, Timeout = TimeSpan.FromSeconds(5) };
        _js = js;
    }

    public string Language { get; private set; } = DefaultLanguage;

    /// <summary>Լեզուն փոխվեց — LocalizedComponentBase-ը լսում է սա և re-render անում։</summary>
    public event Action? Changed;

    /// <summary>Բանալին թարգմանում է; բացակայող բանալին վերադարձնում է ինքն իրեն, որ UI-ում աչքի ընկնի։</summary>
    public string this[string key] =>
        _currentSiteTexts.TryGetValue(key, out var edited) ? edited
        : _current.TryGetValue(key, out var value) ? value
        : _fallbackSiteTexts.TryGetValue(key, out var editedFallback) ? editedFallback
        : _fallback.TryGetValue(key, out var fallback) ? fallback
        : key;

    /// <summary>Նույնը, բայց string.Format-ի placeholder-ներով ({0}, {1}…)։</summary>
    public string this[string key, params object?[] args]
    {
        get
        {
            var template = this[key];
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                // Admin-ի խմբագրած տեքստում փակագծերը սխալ են — ցույց ենք տալիս
                // ինչպես կա, ոչ թե գցում ամբողջ էջը
                return template;
            }
        }
    }

    /// <summary>
    /// Planner-ի կոդավորված բացատրությունը ("Plan.Note.Proteins::4::5") ընթացիկ լեզվով։
    /// "Goal."-ով արգումենտը նույնպես թարգմանվում է։ Կոդավորված չէ (հին պլանի անգլերեն
    /// տեքստ, API-ի սովորական սխալ)՝ վերադարձնում է ինչպես կա — ուստի ErrorAlert-ը
    /// կարող է սա կանչել ամեն հաղորդագրության համար։
    /// </summary>
    public string Note(string? note)
    {
        if (!Gialora.Shared.PlanningNotes.TryDecode(note, out var key, out var args))
            return note ?? string.Empty;

        var formatted = args.Select(FormatNoteArg).ToArray();
        return this[key, formatted];
    }

    /// <summary>"Goal.x" → նպատակի անունը, "Date:2026-10-26" → "26 Հոկ" (ընթացիկ լեզվով)։</summary>
    private object? FormatNoteArg(string arg)
    {
        if (arg.StartsWith("Goal.", StringComparison.Ordinal))
            return this[arg];

        if (arg.StartsWith(Gialora.Shared.PlanningNotes.DateArgPrefix, StringComparison.Ordinal)
            && DateOnly.TryParseExact(arg[Gialora.Shared.PlanningNotes.DateArgPrefix.Length..], "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
            return DayMonth(date);

        return arg;
    }

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
        var fallback = LoadAsync(DefaultLanguage);
        var fallbackSiteTexts = LoadSiteTextsAsync(DefaultLanguage);
        _fallback = await fallback;
        _fallbackSiteTexts = await fallbackSiteTexts;

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
        if (language == DefaultLanguage)
        {
            _current = _fallback;
            _currentSiteTexts = _fallbackSiteTexts;
        }
        else
        {
            var current = LoadAsync(language);
            var siteTexts = LoadSiteTextsAsync(language);
            _current = await current;
            _currentSiteTexts = await siteTexts;
        }

        Language = language;

        try
        {
            // Screen reader-ների ու hyphenation-ի համար <html lang> պիտի համընկնի
            // (index.html-ի gialoraSetLang — eval-ը կարգելեր CSP-ն)
            await _js.InvokeVoidAsync("gialoraSetLang", language);
        }
        catch (JSException)
        {
            // Կոսմետիկ է — անտեսում ենք
        }
    }

    // --- Admin-ի խմբագրած տեքստեր ---

    /// <summary>Ներկառուցված (JSON-ի) տեքստերը՝ առանց override-ների (admin-ի էջի "Built-in" սյունակը)։</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetBuiltInAsync(string language) =>
        IsSupported(language) ? await LoadAsync(language) : new Dictionary<string, string>();

    /// <summary>Admin-ը տեքստ պահեց/վերադարձրեց — նորից ենք բեռնում override-ները և re-render անում։</summary>
    public async Task ReloadSiteTextsAsync()
    {
        _siteTextCache.Clear();
        _fallbackSiteTexts = await LoadSiteTextsAsync(DefaultLanguage);
        _currentSiteTexts = Language == DefaultLanguage ? _fallbackSiteTexts : await LoadSiteTextsAsync(Language);
        Changed?.Invoke();
    }

    private async Task<Dictionary<string, string>> LoadSiteTextsAsync(string language)
    {
        if (_siteTextCache.TryGetValue(language, out var cached))
            return cached;

        Dictionary<string, string> texts;
        try
        {
            texts = await _api.GetFromJsonAsync<Dictionary<string, string>>($"api/site-texts/{language}")
                    ?? new Dictionary<string, string>();
        }
        catch (Exception)
        {
            // API-ն անհասանելի է (offline PWA, deploy) — ներկառուցված տեքստերը բավական են
            texts = new Dictionary<string, string>();
        }

        _siteTextCache[language] = texts;
        return texts;
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
