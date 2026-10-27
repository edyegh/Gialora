// Gialora.Shared/PlanningNotes.cs
namespace Gialora.Shared;

/// <summary>
/// Server-ի գեներացրած, user-ին ցույց տրվող տեքստերը (planner-ի բացատրությունները,
/// պլանների և գնումների ցուցակների անունները) պահվում են ԲԱՆԱԼԻՈՎ, ոչ թե անգլերեն
/// նախադասությամբ, որ client-ը դրանք ցույց տա ընթացիկ լեզվով։
///
/// Ձևաչափը՝ "Plan.Note.Proteins::4::5" — բանալին i18n JSON-ի key-ն է, մնացածը՝ {0}, {1}…
/// արգումենտները։ Արգումենտի տեսակները (L.Note-ը դրանք ձևաչափում է).
///   "Goal.more-iron"   — ինքն էլ բանալի է (նպատակի անունը)
///   "Date:2026-10-26"  — ամսաթիվ, ցույց է տրվում "26 Հոկ"-ի պես՝ ընթացիկ լեզվով
/// Հին տվյալի անգլերեն նախադասությունները decode չեն լինում և ցույց են տրվում ինչպես կան։
/// </summary>
public static class PlanningNotes
{
    public const string Prefix = "Plan.Note.";
    public const string DateArgPrefix = "Date:";
    private const string Separator = "::";

    /// <summary>Բանալիների նախածանցները, որոնք TryDecode-ը ճանաչում է։</summary>
    private static readonly string[] KnownPrefixes = { Prefix, "Plan.Name.", "List.Name." };

    // Պլանի և գնումների ցուցակի անունները՝ ըստ պլանի տեսակի (արգումենտը՝ սկզբի ամսաթիվը)
    public const string PlanNameWeekly = "Plan.Name.Weekly";
    public const string PlanNameDaily = "Plan.Name.Daily";
    public const string PlanNameBatch = "Plan.Name.Batch";
    public const string PlanNameBiweekly = "Plan.Name.Biweekly";
    public const string ListNameWeekly = "List.Name.Weekly";
    public const string ListNameDaily = "List.Name.Daily";
    public const string ListNameBatch = "List.Name.Batch";
    public const string ListNameBiweekly = "List.Name.Biweekly";

    public static string DateArg(DateOnly date) =>
        DateArgPrefix + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public const string NoMatches = Prefix + "NoMatches";
    public const string BatchOnly = Prefix + "BatchOnly";
    public const string NoBatchRecipes = Prefix + "NoBatchRecipes";
    public const string PartialPlan = Prefix + "PartialPlan";
    public const string Proteins = Prefix + "Proteins";
    public const string VegetableMeals = Prefix + "VegetableMeals";
    public const string LegumeMeals = Prefix + "LegumeMeals";
    public const string SharedIngredients = Prefix + "SharedIngredients";
    public const string Freezable = Prefix + "Freezable";
    public const string GoalMatched = Prefix + "GoalMatched";
    public const string GoalNone = Prefix + "GoalNone";

    public static string Encode(string key, params object[] args) =>
        args.Length == 0 ? key : key + Separator + string.Join(Separator, args);

    public static bool TryDecode(string? note, out string key, out string[] args)
    {
        key = string.Empty;
        args = Array.Empty<string>();

        if (string.IsNullOrEmpty(note) || !KnownPrefixes.Any(p => note.StartsWith(p, StringComparison.Ordinal)))
            return false;

        var parts = note.Split(Separator);
        key = parts[0];
        args = parts[1..];
        return true;
    }
}
