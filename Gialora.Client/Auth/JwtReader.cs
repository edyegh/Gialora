// Gialora.Client/Auth/JwtReader.cs
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Gialora.Client.Auth;

/// <summary>
/// Կարդում է JWT-ի payload-ը՝ claim-երը և ժամկետը։
///
/// Ինչու ձեռքով, և ոչ թե System.IdentityModel.Tokens.Jwt-ով.
/// այդ package-ը browser-ի payload-ի մեջ բերում էր ~5 ՄԲ ավելորդ assembly
/// (System.Private.Xml 3 ՄԲ, System.Data.Common 1 ՄԲ, ամբողջ System.Xml
/// ընտանիքը, RegularExpressions) — SAML-ի և XML token-ների համար, որոնք
/// այս հավելվածը երբեք չի օգտագործում։
///
/// Client-ը token-ը ՉԻ ՎԱՎԵՐԱՑՆՈՒՄ և չպիտի վավերացնի — ստորագրությունը
/// ստուգում է API-ն։ Այստեղ պետք է միայն՝ ո՞վ է user-ը և ե՞րբ է լրանում token-ը,
/// որ UI-ն ճիշտ բան ցույց տա։ Ուստի base64url + JSON-ը լիովին բավարար է։
/// </summary>
public static class JwtReader
{
    public record JwtInfo(IReadOnlyList<Claim> Claims, DateTime ExpiresUtc);

    /// <summary>Վերադարձնում է null, եթե token-ը վնասված է կամ ձևաչափը սխալ է։</summary>
    public static JwtInfo? Read(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
                return null;

            using var document = JsonDocument.Parse(Decode(parts[1]));
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var claims = new List<Claim>();
            var expires = DateTime.MinValue;

            foreach (var property in root.EnumerateObject())
            {
                // "exp"-ը Unix seconds է (RFC 7519)
                if (property.NameEquals("exp") && property.Value.TryGetInt64(out var exp))
                    expires = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;

                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.Array:
                        // Դերերը հաճախ զանգված են — ամեն տարր առանձին claim է,
                        // այլապես IsInRole()-ը չի աշխատի
                        foreach (var item in property.Value.EnumerateArray())
                            claims.Add(new Claim(property.Name, ToText(item)));
                        break;

                    case JsonValueKind.Object:
                        claims.Add(new Claim(property.Name, property.Value.GetRawText()));
                        break;

                    default:
                        claims.Add(new Claim(property.Name, ToText(property.Value)));
                        break;
                }
            }

            return new JwtInfo(claims, expires);
        }
        catch (Exception)
        {
            // Վնասված token — կանչողը այն մաքրում է
            return null;
        }
    }

    private static string ToText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        _ => element.GetRawText()
    };

    /// <summary>
    /// base64url → բայթեր։ JWT-ն օգտագործում է '-' և '_' ('+'/'/'-ի փոխարեն) և
    /// բաց է թողնում padding-ը, ուստի Convert.FromBase64String-ը ուղիղ չի աշխատում։
    /// </summary>
    private static byte[] Decode(string segment)
    {
        var value = segment.Replace('-', '+').Replace('_', '/');

        value += (value.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty
        };

        return Convert.FromBase64String(value);
    }

    /// <summary>Ստուգում է միայն չափսը՝ ոչ թե բովանդակությունը (UTF-8 JSON է սպասվում)։</summary>
    public static string ToUtf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
