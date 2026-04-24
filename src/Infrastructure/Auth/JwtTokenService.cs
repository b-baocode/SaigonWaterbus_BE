using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _jwtOptions;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtOptions> jwtOptions, TimeProvider timeProvider)
    {
        _jwtOptions = jwtOptions.Value;
        _timeProvider = timeProvider;
    }

    public AccessTokenResult GenerateAccessToken(
        int userId,
        string phoneNumber,
        string email,
        IReadOnlyCollection<string> roleSystemNames)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_jwtOptions.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.MobilePhone, phoneNumber),
            new(ClaimTypes.Email, email)
        };

        claims.AddRange(roleSystemNames
            .Distinct(StringComparer.Ordinal)
            .Select(role => new Claim(ClaimTypes.Role, role)));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessTokenResult(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }

    public string GenerateRefreshTokenSecret() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public DateTimeOffset GetRefreshTokenExpiry() =>
        _timeProvider.GetUtcNow().AddDays(_jwtOptions.RefreshTokenDays);
}
