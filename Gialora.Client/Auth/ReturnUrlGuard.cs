// Gialora.Client/Auth/ReturnUrlGuard.cs
namespace Gialora.Client.Auth;

/// <summary>
/// Login-ից հետո վերադարձի հասցեի ստուգումը։ NavigationManager.NavigateTo-ն
/// base-ից դուրս ցանկացած URL-ը տալիս է location.replace-ին, ուստի
/// "javascript:..."-ը կկատարվեր մեր origin-ում (token-ը localStorage-ում է),
/// իսկ "\\evil.example"-ը browser-ը կդարձներ "https://evil.example/"։
/// Թույլատրում ենք ՄԻԱՅՆ base-ի նկատմամբ հարաբերական path՝ սպիտակ ցուցակի նիշերով։
/// </summary>
public static class ReturnUrlGuard
{
    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return string.Empty;

        var target = returnUrl.Trim();

        // "/x", "//host", "\host" — բացարձակ կամ protocol-relative
        if (target[0] is '/' or '\\')
            return string.Empty;

        // Scheme ("javascript:", "https:", "data:") կամ backslash-ով շրջանցում
        if (target.Contains(':') || target.Contains('\\') || target.Contains("//"))
            return string.Empty;

        foreach (var c in target)
        {
            var allowed = char.IsAsciiLetterOrDigit(c) || "-._~/?#=&%+".Contains(c);
            if (!allowed)
                return string.Empty;
        }

        return target;
    }
}
