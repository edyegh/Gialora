// Gialora.Application/Services/IFamilyService.cs
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IFamilyService
{
    Task<FamilyDto> GetOrCreateFamilyAsync(Guid userId);
    Task<FamilyDto> RenameAsync(Guid userId, FamilyUpdateDto dto);

    /// <summary>"Selects preferences" քայլը (app structure §1)։</summary>
    Task<FamilyPreferencesDto> UpdatePreferencesAsync(Guid userId, FamilyPreferencesDto dto);

    Task<FamilyMemberDto> AddMemberAsync(Guid userId, FamilyMemberCreateDto dto);
    Task<FamilyMemberDto?> UpdateMemberAsync(Guid userId, Guid memberId, FamilyMemberUpdateDto dto);
    Task<bool> RemoveMemberAsync(Guid userId, Guid memberId);

    /// <summary>Meal-planning-ի և shopping list-ի համար — user → familyId։</summary>
    Task<Guid> GetFamilyIdAsync(Guid userId);
}
