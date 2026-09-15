// Gialora.Application/Services/UserAdminService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public class UserAdminService : IUserAdminService
{
    private readonly GialoraDbContext _db;
    private readonly ILogger<UserAdminService> _logger;

    public UserAdminService(GialoraDbContext db, ILogger<UserAdminService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<AdminUserDto>> ListAsync(Guid currentAdminId)
    {
        // Entity-ները նախ բերում ենք, հետո map անում — Role.ToString()-ը SQL-ի չի թարգմանվում
        var users = await _db.Users.AsNoTracking()
            .OrderBy(u => u.Role == UserRole.Admin ? 0 : 1)
            .ThenBy(u => u.Email)
            .ToListAsync();

        return users.Select(u => ToDto(u, currentAdminId)).ToList();
    }

    public async Task<AdminUserDto> CreateAsync(AdminUserCreateDto dto, Guid currentAdminId)
    {
        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out var role))
            throw new ValidationFailedException("Role must be either User or Admin.");

        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

        // IgnoreQueryFilters — soft-delete-ված user-ի email-ը դեռ unique index-ի տակ է,
        // առանց սրա SaveChanges-ը կբռներ DbUpdateException-ը, ոչ թե պարզ 409-ը
        var emailTaken = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == normalizedEmail);
        if (emailTaken)
            throw new ConflictException("An account with this email already exists.");

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 12),
            DisplayName = dto.DisplayName.Trim(),
            Role = role,
            EmailConfirmed = true // admin-ի ձեռքով ստեղծածը հաստատման կարիք չունի
        };

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.Entry(user).State = EntityState.Detached;
            throw new ConflictException("An account with this email already exists.");
        }

        _logger.LogInformation("Admin {AdminId} created user {UserId} with role {Role}", currentAdminId, user.Id, role);

        return ToDto(user, currentAdminId);
    }

    public async Task SetPasswordAsync(Guid userId, AdminSetPasswordDto dto)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new NotFoundException("User not found.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, workFactor: 12);
        user.UpdatedAtUtc = DateTime.UtcNow;

        // Նոր password-ը նշանակում է՝ հին lockout-ը այլևս իմաստ չունի
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Password reset by admin for user {UserId}", userId);
    }

    public async Task DeleteAsync(Guid userId, Guid currentAdminId)
    {
        if (userId == currentAdminId)
            throw new ValidationFailedException("You cannot remove your own account.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new NotFoundException("User not found.");

        if (user.Role == UserRole.Admin)
        {
            var otherAdmins = await _db.Users.CountAsync(u => u.Role == UserRole.Admin && u.Id != userId);
            if (otherAdmins == 0)
                throw new ConflictException("Cannot remove the last admin account.");
        }

        // Soft delete — recipe-ները/blog-ի գրառումները (Restrict FK) մնում են, user-ը
        // անհետանում է query filter-ի շնորհիվ և այլևս login անել չի կարող
        user.IsDeleted = true;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Admin {AdminId} removed user {UserId}", currentAdminId, userId);
    }

    private static AdminUserDto ToDto(User u, Guid currentAdminId) => new()
    {
        Id = u.Id,
        Email = u.Email,
        DisplayName = u.DisplayName,
        Role = u.Role.ToString(),
        CreatedAtUtc = u.CreatedAtUtc,
        IsLockedOut = u.LockoutEndUtc is not null && u.LockoutEndUtc > DateTime.UtcNow,
        HasFamily = u.FamilyId is not null,
        IsCurrentUser = u.Id == currentAdminId
    };
}
