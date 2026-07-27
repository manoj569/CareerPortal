using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace JobPortal.Infrastructure.Authentication;

public sealed class JwtTokenService(IConfiguration configuration, TimeProvider timeProvider) : IJwtTokenService
{
    private readonly string _issuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("JWT issuer is not configured.");
    private readonly string _audience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("JWT audience is not configured.");
    private readonly string _key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT signing key is not configured.");
    private readonly int _accessTokenLifetimeMinutes = configuration.GetValue<int?>("Jwt:AccessTokenLifetimeMinutes") ?? 15;

    public AccessTokenResult CreateAccessToken(User user)
    {
        var expiresAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(_accessTokenLifetimeMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.Name)
        };
        var signingCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key)), SecurityAlgorithms.HmacSha512);
        var token = new JwtSecurityToken(_issuer, _audience, claims, expires: expiresAtUtc, signingCredentials: signingCredentials);
        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }

    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
