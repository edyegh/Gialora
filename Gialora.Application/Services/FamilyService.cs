// Gialora.Application/Services/FamilyService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;

namespace Gialora.Application.Services;

public class FamilyService : IFamilyService
{
    private readonly GialoraDbContext _db;
    private readonly ILogger<FamilyService> _logger;

    public FamilyService(GialoraDbContext db, ILogger<FamilyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<FamilyDto> GetOrCreateFamilyAsync(Guid userId)
    {
        var user = await _db.Users
            .Include(u => u.Family)
                .ThenInclude(f => f!.FamilyMembers)
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new NotFoundException("User not found.");

        // Առաջին onboarding քայլում Family-ն ստեղծվում է ավտոմատ
        if (user.Family is null)
        {
            var family = new Family { Name = $"{user.DisplayName}'s Family" };
            _db.Families.Add(family);
            user.Family = family;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Family {FamilyId} auto-created for user {UserId}", family.Id, userId);
            return Map(family);
        }

        return Map(user.Family);
    }

    public async Task<FamilyDto> RenameAsync(Guid userId, FamilyUpdateDto dto)
    {
        var family = await LoadFamilyAsync(userId);
        family.Name = dto.Name.Trim();
        await _db.SaveChangesAsync();
        return Map(family);
    }

    public async Task<FamilyPreferencesDto> UpdatePreferencesAsync(Guid userId, FamilyPreferencesDto dto)
    {
        var family = await LoadFamilyAsync(userId);

        family.PreferredCuisine = dto.PreferredCuisine;
        family.MaxCookingTimeMinutes = Math.Clamp(dto.MaxCookingTimeMinutes, 10, 300);
        family.CookingDaysPerWeek = Math.Clamp(dto.CookingDaysPerWeek, 1, 7);
        family.ServingsPerMeal = Math.Clamp(dto.ServingsPerMeal, 1, 20);
        family.Budget = dto.Budget;
        family.DietPreference = dto.DietPreference;
        family.PreferFreezerFriendly = dto.PreferFreezerFriendly;

        // Enum-երը CSV-ում պահվում են ԱՆՈՒՆՈՎ, ոչ թե թվով — DB-ն ընթեռնելի է մնում
        // և enum-ի արժեքների վերադասավորումը հին տողերը չի փչացնում։
        family.ExcludedProteins = dto.ExcludedProteins.Distinct().Select(p => p.ToString()).ToList();
        family.DislikedIngredients = NormalizeList(dto.DislikedIngredients);

        await _db.SaveChangesAsync();
        return MapPreferences(family);
    }

    public async Task<FamilyMemberDto> AddMemberAsync(Guid userId, FamilyMemberCreateDto dto)
    {
        var familyId = await GetFamilyIdAsync(userId);

        var member = new FamilyMember { FamilyId = familyId };
        ApplyMember(member, dto);

        _db.FamilyMembers.Add(member);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Family member {MemberId} added to family {FamilyId}", member.Id, familyId);
        return MapMember(member);
    }

    public async Task<FamilyMemberDto?> UpdateMemberAsync(Guid userId, Guid memberId, FamilyMemberUpdateDto dto)
    {
        var familyId = await GetFamilyIdAsync(userId);

        // Ստուգում ենք և՛ memberId-ն, և՛ որ պատկանում է ՀԵՆՑ ԱՅՍ user-ի Family-ին
        var member = await _db.FamilyMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.FamilyId == familyId);

        if (member is null)
            return null; // կամ չկա, կամ ուրիշի ընտանիքինն է — երկուսն էլ 404

        ApplyMember(member, dto);
        await _db.SaveChangesAsync();
        return MapMember(member);
    }

    public async Task<bool> RemoveMemberAsync(Guid userId, Guid memberId)
    {
        var familyId = await GetFamilyIdAsync(userId);

        var member = await _db.FamilyMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.FamilyId == familyId);

        if (member is null)
            return false;

        member.IsDeleted = true; // soft delete
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<Guid> GetFamilyIdAsync(Guid userId)
    {
        var familyId = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FamilyId)
            .FirstOrDefaultAsync();

        if (familyId is null)
        {
            // Ավտոմատ ստեղծում ենք, փոխանակ սխալ նետելու. client-ը կարող է
            // meal plan խնդրել՝ երբեք /family/me չկանչած (օր. deep link-ից)։
            var created = await GetOrCreateFamilyAsync(userId);
            return created.Id;
        }

        return familyId.Value;
    }

    // --- Private helpers ---

    private async Task<Family> LoadFamilyAsync(Guid userId)
    {
        var familyId = await GetFamilyIdAsync(userId);

        return await _db.Families
            .Include(f => f.FamilyMembers)
            .FirstOrDefaultAsync(f => f.Id == familyId)
            ?? throw new NotFoundException("Family not found.");
    }

    private static void ApplyMember(FamilyMember member, FamilyMemberCreateDto dto)
    {
        member.Name = dto.Name.Trim();
        member.MemberType = dto.MemberType;
        member.Age = dto.Age;
        member.AgeMonths = dto.AgeMonths;
        member.DietaryRestrictions = NormalizeList(dto.DietaryRestrictions);
        member.Allergies = NormalizeList(dto.Allergies);
        member.Goals = dto.Goals.Select(g => g.Trim()).Where(g => g.Length > 0).Distinct().ToList();
    }

    private static List<string> NormalizeList(IEnumerable<string> items) =>
        items.Select(i => i.Trim().ToLowerInvariant())
             .Where(i => i.Length > 0)
             .Distinct()
             .ToList();

    private static FamilyDto Map(Family family) => new()
    {
        Id = family.Id,
        Name = family.Name,
        Members = family.FamilyMembers
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.MemberType)
            .ThenBy(m => m.Name)
            .Select(MapMember)
            .ToList(),
        Preferences = MapPreferences(family)
    };

    private static FamilyPreferencesDto MapPreferences(Family family) => new()
    {
        PreferredCuisine = family.PreferredCuisine,
        MaxCookingTimeMinutes = family.MaxCookingTimeMinutes,
        CookingDaysPerWeek = family.CookingDaysPerWeek,
        ServingsPerMeal = family.ServingsPerMeal,
        Budget = family.Budget,
        DietPreference = family.DietPreference,
        PreferFreezerFriendly = family.PreferFreezerFriendly,
        ExcludedProteins = family.ExcludedProteins
            .Select(p => Enum.TryParse<ProteinType>(p, true, out var parsed) ? parsed : (ProteinType?)null)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .ToList(),
        DislikedIngredients = family.DislikedIngredients
    };

    private static FamilyMemberDto MapMember(FamilyMember m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        MemberType = m.MemberType,
        Age = m.Age,
        AgeMonths = m.AgeMonths,
        DietaryRestrictions = m.DietaryRestrictions,
        Allergies = m.Allergies,
        Goals = m.Goals
    };
}
