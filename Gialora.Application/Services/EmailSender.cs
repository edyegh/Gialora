// Gialora.Application/Services/EmailSender.cs
namespace Gialora.Application.Services;

public record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Email-ի ուղարկումը։ Իրականացումը (SMTP) Api-ում է, ինչպես Telegram-ինը — Application-ը
/// կախված չէ կոնկրետ provider-ից, իսկ թեստերը fake են տալիս։ Ձախողման դեպքում նետում է։
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Հաստատման կոդի email-ը՝ UI-ի երեք լեզուներով (անհայտ լեզու → անգլերեն)։</summary>
public static class VerificationEmail
{
    private sealed record Template(string Subject, string Greeting, string Intro, string Expiry, string Ignore);

    private static readonly Dictionary<string, Template> Templates = new()
    {
        ["en"] = new(
            "Your Gialora verification code",
            "Hi {0},",
            "Use this code to confirm your email address and finish creating your Gialora account:",
            "The code is valid for {0} minutes.",
            "If you didn't create an account, you can safely ignore this email."),
        ["hy"] = new(
            "Gialora — հաստատման կոդ",
            "Բարև, {0},",
            "Մուտքագրեք այս կոդը՝ email-ը հաստատելու և Gialora-ի գրանցումն ավարտելու համար.",
            "Կոդը վավեր է {0} րոպե։",
            "Եթե դուք չեք գրանցվել, պարզապես անտեսեք այս նամակը։"),
        ["el"] = new(
            "Ο κωδικός επαλήθευσης Gialora",
            "Γεια σας {0},",
            "Χρησιμοποιήστε αυτόν τον κωδικό για να επιβεβαιώσετε το email σας και να ολοκληρώσετε τη δημιουργία του λογαριασμού σας στο Gialora:",
            "Ο κωδικός ισχύει για {0} λεπτά.",
            "Αν δεν δημιουργήσατε λογαριασμό, μπορείτε να αγνοήσετε αυτό το email.")
    };

    public static EmailMessage Build(string to, string displayName, string code, string? language, int validMinutes)
    {
        var t = Templates.GetValueOrDefault(language?.Trim().ToLowerInvariant() ?? "en") ?? Templates["en"];
        var greeting = string.Format(t.Greeting, displayName);
        var expiry = string.Format(t.Expiry, validMinutes);

        var text = $"{greeting}\n\n{t.Intro}\n\n{code}\n\n{expiry}\n\n{t.Ignore}\n\n— Gialora";

        // displayName-ը user-ի մուտքն է — HTML-ում encode ենք
        var html = $"""
            <div style="font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#2b2b2b;max-width:480px;">
              <p>{System.Net.WebUtility.HtmlEncode(greeting)}</p>
              <p>{System.Net.WebUtility.HtmlEncode(t.Intro)}</p>
              <p style="font-size:30px;font-weight:bold;letter-spacing:8px;margin:24px 0;">{code}</p>
              <p>{System.Net.WebUtility.HtmlEncode(expiry)}</p>
              <p style="color:#777;font-size:13px;">{System.Net.WebUtility.HtmlEncode(t.Ignore)}</p>
              <p>— Gialora</p>
            </div>
            """;

        return new EmailMessage(to, t.Subject, html, text);
    }
}
