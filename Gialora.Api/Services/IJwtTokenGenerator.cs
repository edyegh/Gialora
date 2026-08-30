// Gialora.Api/Services/IJwtTokenGenerator.cs
using Gialora.Shared.Dtos;

namespace Gialora.Api.Services;

public interface IJwtTokenGenerator
{
    string GenerateToken(AuthResultDto user);
}