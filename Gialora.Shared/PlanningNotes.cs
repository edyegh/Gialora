// Gialora.Shared/PlanningNotes.cs
namespace Gialora.Shared;

/// <summary>
/// Planner-ի բացատրությունները ("5 different protein sources...") պահվում են ԲԱՆԱԼԻՈՎ,
/// ոչ թե անգլերեն նախադասությամբ, որ client-ը դրանք ցույց տա ընթացիկ լեզվով։
///
/// Ձևաչափը՝ "Plan.Note.Proteins::4::5" — բանալին i18n JSON-ի key-ն է, մնացածը՝ {0}, {1}…
/// արգումենտները։ "Goal."-ով սկսվող արգումենտն ինքն էլ բանալի է (նպատակի անունը)։
/// Հին պլանների անգլերեն նախադասությունները decode չեն լինում և ցույց են տրվում ինչպես կան։
/// </summary>
public static class PlanningNotes
{
    public const string Prefix = "Plan.Note.";
    private const string Separator = "::";

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

        if (string.IsNullOrEmpty(note) || !note.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = note.Split(Separator);
        key = parts[0];
        args = parts[1..];
        return true;
    }
}
