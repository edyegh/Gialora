// Gialora.Application/Services/IFamilyService.cs
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IFamilyService
{
    Task<FamilyDto> GetOrCreateFamilyAsync(Guid userId);
    Task<FamilyMemberDto> AddMemberAsync(Guid userId, FamilyMemberCreateDto dto);
    Task<FamilyMemberDto?> UpdateMemberAsync(Guid userId, Guid memberId, FamilyMemberUpdateDto dto);
    Task<bool> RemoveMemberAsync(Guid userId, Guid memberId);
}