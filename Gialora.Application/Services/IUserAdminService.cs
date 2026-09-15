// Gialora.Application/Services/IUserAdminService.cs
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

/// <summary>Admin-ի user management-ը՝ ցուցակ, ավելացնել, password փոխել, հեռացնել։</summary>
public interface IUserAdminService
{
    Task<List<AdminUserDto>> ListAsync(Guid currentAdminId);
    Task<AdminUserDto> CreateAsync(AdminUserCreateDto dto, Guid currentAdminId);
    Task SetPasswordAsync(Guid userId, AdminSetPasswordDto dto);
    Task DeleteAsync(Guid userId, Guid currentAdminId);
}
