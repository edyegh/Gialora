// Gialora.Api/Extensions/ClaimsPrincipalExtensions.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Gialora.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Վերադարձնում է token-ի "sub" claim-ը որպես Guid, կամ null՝ եթե բացակայում է/վնասված է։
    /// Նախկինում controller-ները անում էին Guid.Parse(claim!) — բացակայող claim-ը 500 էր տալիս 401-ի փոխարեն։
    /// </summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
