// Gialora.Api/Services/ActiveUserValidator.cs
using Gialora.Api.Extensions;
using Gialora.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Gialora.Api.Services;

/// <summary>
/// Token-ի ստորագրությունը վավեր է, բայց user-ը դեռ կա՞ և նույն role-ո՞վ։ Առանց սրա
/// ջնջված (կամ admin-ից զրկված) user-ի token-ը, admin-ի իրավունքներով հանդերձ,
/// աշխատում էր մինչև expiry (60 րոպե)։ Արդյունքը cache-ում ենք կարճ ժամանակով, որ
/// ամեն request DB round-trip չանի — ջնջումն ուժի մեջ է մտնում առավելագույնը CacheFor-ից հետո։
/// </summary>
public static class ActiveUserValidator
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        var userId = context.Principal?.GetUserId();
        if (userId is null)
        {
            context.Fail("Token has no user id.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var cache = services.GetRequiredService<IMemoryCache>();

        var currentRole = await cache.GetOrCreateAsync($"active-user:{userId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var db = services.GetRequiredService<GialoraDbContext>();

            // Query filter-ը (IsDeleted) ջնջվածին բաց է թողնում → null
            var role = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => (Gialora.Data.Entities.UserRole?)u.Role)
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
            return role?.ToString();
        });

        var tokenRole = context.Principal!.FindFirst(JwtTokenGenerator.RoleClaimType)?.Value;
        if (currentRole is null || !string.Equals(currentRole, tokenRole, StringComparison.Ordinal))
            context.Fail("User no longer exists or their role changed.");
    }
}
