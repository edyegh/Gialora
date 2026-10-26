// Gialora.Application/Services/IAuthService.cs
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IAuthService
{
    /// <summary>Ստեղծում է ՉՀԱՍՏԱՏՎԱԾ account և email-ով ուղարկում հաստատման կոդը։</summary>
    Task<RegisterResultDto> RegisterAsync(RegisterDto dto);

    /// <summary>Ճիշտ կոդ → account-ը հաստատված է։ Սխալ/ժամկետանց → ValidationFailedException։</summary>
    Task<AuthResultDto> VerifyEmailAsync(VerifyEmailDto dto);

    /// <summary>Նոր կոդ (cooldown-ով)։ Լուռ է անհայտ կամ արդեն հաստատված email-ի համար։</summary>
    Task ResendVerificationAsync(ResendVerificationDto dto);

    /// <summary>Չհաստատված email-ի դեպքում նետում է EmailNotConfirmedException։</summary>
    Task<AuthResultDto?> ValidateCredentialsAsync(LoginDto dto);

    /// <summary>Token-ի վավերականությունը ստուգելու համար (GET /api/auth/me)։</summary>
    Task<AuthResultDto?> GetByIdAsync(Guid userId);
}
