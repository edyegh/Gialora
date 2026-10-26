// Gialora.Data/Entities/UserActivity.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

/// <summary>
/// Մեկ գործողություն (էջի դիտում / click / ռեցեպտի դիտում) admin-ի վիճակագրության համար։
/// Գրանցվում է միայն cookie-ների "Accept all" համաձայնությունից հետո (client-ի կողմում)։
/// UserId-ն null է հյուրի համար։ Admin-ների գործողությունները չեն գրանցվում։
/// </summary>
public class UserActivity : BaseEntity
{
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public ActivityType Type { get; set; }

    public string? Target { get; set; }

    public string? Path { get; set; }

    /// <summary>Առանց FK — ջնջված ռեցեպտի դիտումները պատմության մեջ մնում են, կարդալիս join ենք անում։</summary>
    public Guid? RecipeId { get; set; }
}
