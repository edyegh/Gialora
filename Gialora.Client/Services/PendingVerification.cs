// Gialora.Client/Services/PendingVerification.cs
namespace Gialora.Client.Services;

/// <summary>
/// Register/Login-ից verify-email էջ փոխանցում է password-ը (API-ն այն պահանջում է
/// կոդի հետ միասին)։ ՄԻԱՅՆ հիշողությունում — ոչ URL, ոչ localStorage։ Էջը reload
/// անելիս դատարկ է, և verify էջը password-ը ինքն է հարցնում։ Singleton է, որ
/// navigation-ի ընթացքում չկորչի։
/// </summary>
public class PendingVerification
{
    private string? _email;
    private string? _password;

    public void Set(string email, string password)
    {
        _email = email.Trim();
        _password = password;
    }

    /// <summary>Password-ը, եթե պահված է հենց այս email-ի համար։</summary>
    public string? PasswordFor(string? email) =>
        _email is not null && string.Equals(_email, email?.Trim(), StringComparison.OrdinalIgnoreCase) ? _password : null;

    public void Clear()
    {
        _email = null;
        _password = null;
    }
}
