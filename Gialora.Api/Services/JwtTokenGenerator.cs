// Gialora.Api/Services/JwtTokenGenerator.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Services;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    // Claim-ների անունները դիտավորյալ "կարճ" են (ոչ ClaimTypes.* URI-ներ)։
    // JwtSecurityTokenHandler-ը ClaimTypes.Name/Role-ը լուռ ձևափոխում է "unique_name"/"role"-ի,
    // ինչի պատճառով client-ը հետո չէր գտնում դրանք։ Հիմա երկու կողմն էլ նույն անուններն են օգտագործում։
    public const string NameClaimType = "name";
    public const string RoleClaimType = "role";

    private readonly IConfiguration _config;

    public JwtTokenGenerator(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(AuthResultDto user)
    {
        var signingKey = _config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("JWT signing key is not configured.");
        var issuer = _config["Jwt:Issuer"];
        var audience = _config["Jwt:Audience"];

        if (!int.TryParse(_config["Jwt:ExpiryMinutes"], out var expiryMinutes) || expiryMinutes <= 0)
            expiryMinutes = 60;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(NameClaimType, user.DisplayName),
            new(RoleClaimType, user.Role), // ← սա է թույլ տալու [Authorize(Roles = "Admin")]
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) // unique token id
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var handler = new JwtSecurityTokenHandler();
        // Առանց սրա handler-ը մեր կարճ անունները հետ կձևափոխեր ClaimTypes.* URI-ների
        handler.OutboundClaimTypeMap.Clear();

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return handler.WriteToken(token);
    }
}
