namespace SaigonWaterbus.Application.Common.Interfaces;

public interface ITokenService
{
    AccessTokenResult GenerateAccessToken(
        Guid userId,
        string? phoneNumber,
        string? email,
        IReadOnlyCollection<string> roleSystemNames);

    string GenerateRefreshTokenSecret();

    DateTimeOffset GetRefreshTokenExpiry();
}

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);
