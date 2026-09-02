// Gialora.Application/Services/FamilyService.cs
using Microsoft.EntityFrameworkCore;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;
using Microsoft.Extensions.Logging;

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
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
            throw new InvalidOperationException("User not found.");

        // Եթե user-ը դեռ Family չունի, ստեղծում ենք ավտոմատ (առաջին onboarding քայլում)
        if (user.Family is null)
        {
            var family = new Family { Name = $"{user.DisplayName}'s Family" };
            _db.Families.Add(family);
            user.Family = family;
            await _db.SaveChangesAsync();

            return new FamilyDto { Id = family.Id, Name = family.Name, Members = new() };
        }

        return new FamilyDto
        {
            Id = user.Family.Id,
            Name = user.Family.Name,
            Members = user.Family.FamilyMembers.Select(MapToDto).ToList()
        };
    }

    public async Task<FamilyMemberDto> AddMemberAsync(Guid userId, FamilyMemberCreateDto dto)
    {
        var familyId = await GetFamilyIdForUserAsync(userId);

        var member = new FamilyMember
        {
            FamilyId = familyId,
            Name = dto.Name.Trim(),
            Age = dto.Age,
            DietaryRestrictions = NormalizeList(dto.DietaryRestrictions),
            Allergies = NormalizeList(dto.Allergies)
        };

        _db.FamilyMembers.Add(member);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Family member {MemberId} added to family {FamilyId}", member.Id, familyId);
        return MapToDto(member);
    }

    public async Task<FamilyMemberDto?> UpdateMemberAsync(Guid userId, Guid memberId, FamilyMemberUpdateDto dto)
    {
        var familyId = await GetFamilyIdForUserAsync(userId);

        var member = await _db.FamilyMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.FamilyId == familyId);

        // ⚠️ Կարևոր. ստուգում ենք և՛ memberId-ն, և՛ որ պատկանում է ՀԵՆՑ ԱՅՍ user-ի Family-ին
        if (member is null)
            return null; // կամ member գոյություն չունի, կամ ուրիշի ընտանիքինն է — երկուսն էլ 404

        member.Name = dto.Name.Trim();
        member.Age = dto.Age;
        member.DietaryRestrictions = NormalizeList(dto.DietaryRestrictions);
        member.Allergies = NormalizeList(dto.Allergies);
        member.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return MapToDto(member);
    }

    public async Task<bool> RemoveMemberAsync(Guid userId, Guid memberId)
    {
        var familyId = await GetFamilyIdForUserAsync(userId);

        var member = await _db.FamilyMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.FamilyId == familyId);

        if (member is null)
            return false;

        member.IsDeleted = true; // soft delete
        member.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    // --- Private helpers ---

    private async Task<Guid> GetFamilyIdForUserAsync(Guid userId)
    {
        var familyId = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FamilyId)
            .FirstOrDefaultAsync();

        if (familyId is null)
            throw new InvalidOperationException("User does not belong to a family yet. Call GetOrCreateFamily first.");

        return familyId.Value;
    }

    private static List<string> NormalizeList(List<string> items) =>
        items.Select(i => i.Trim().ToLowerInvariant()).Distinct().ToList();

    private static FamilyMemberDto MapToDto(FamilyMember m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        Age = m.Age,
        DietaryRestrictions = m.DietaryRestrictions,
        Allergies = m.Allergies
    };
}