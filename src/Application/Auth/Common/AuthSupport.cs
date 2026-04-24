using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Common;

internal static class AuthSupport
{
    public static global::SaigonWaterbus.Application.Common.Exceptions.ValidationException CreateValidationException(string propertyName, string errorMessage) =>
        new([new ValidationFailure(propertyName, errorMessage)]);

    public static async Task<IReadOnlyCollection<Role>> GetActiveRolesAsync(
        IApplicationDbContext context,
        int userId,
        CancellationToken cancellationToken)
    {
        return await context.Set<UserRoleAssignment>()
            .Where(x => x.UserId == userId && x.IsActive)
            .Select(x => x.Role)
            .ToListAsync(cancellationToken);
    }

    public static AuthSessionDto CreateSessionDto(
        User user,
        IReadOnlyCollection<Role> roles,
        AccessTokenResult accessToken,
        string refreshToken,
        DateTimeOffset refreshTokenExpiresAt)
    {
        var roleDtos = roles
            .Select(x => new AuthRoleDto(x.Code, x.SystemName, x.DisplayName))
            .ToArray();

        return new AuthSessionDto(
            new AuthUserDto(
                user.Id,
                user.UserCode,
                user.FullName,
                user.DateOfBirth,
                user.PhoneNumber,
                user.Email,
                user.Status,
                roleDtos),
            new AuthTokensDto(
                accessToken.Token,
                accessToken.ExpiresAt,
                refreshToken,
                refreshTokenExpiresAt));
    }

    public static void EnsureUserCanLogin(User user, string propertyName = "email")
    {
        if (user.Status == UserStatus.PendingVerification)
        {
            throw CreateValidationException(propertyName, "Account has not completed OTP verification.");
        }

        if (user.Status == UserStatus.Suspended)
        {
            throw CreateValidationException(propertyName, "Account is suspended.");
        }
    }

    public static async Task RevokeActiveRefreshTokensAsync(
        IApplicationDbContext context,
        int userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        var refreshTokens = await context.Set<RefreshToken>()
            .Where(x => x.UserId == userId
                     && x.RevokedAt == null
                     && x.ExpiresAt > revokedAt)
            .ToListAsync(cancellationToken);

        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.RevokedAt = revokedAt;
        }
    }

    public static async Task RetirePendingOtpChallengesAsync(
        IApplicationDbContext context,
        int userId,
        OtpPurpose purpose,
        DateTimeOffset retiredAt,
        CancellationToken cancellationToken)
    {
        var pendingChallenges = await context.Set<OtpChallenge>()
            .Where(x => x.UserId == userId
                     && x.Purpose == purpose
                     && x.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var pendingChallenge in pendingChallenges)
        {
            pendingChallenge.ConsumedAt = retiredAt;
        }
    }

    public static async Task EnsureCurrentUserIsAdminAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.UserId.HasValue)
        {
            throw new UnauthorizedAccessException();
        }

        var isAdmin = await context.Set<UserRoleAssignment>()
            .AnyAsync(
                x => x.UserId == userContext.UserId.Value
                  && x.IsActive
                  && x.Role.SystemName == Roles.AdminSystemName,
                cancellationToken);

        if (!isAdmin)
        {
            throw new ForbiddenAccessException();
        }
    }

    public static string FormatRefreshToken(int refreshTokenId, string secret) => $"{refreshTokenId}.{secret}";

    public static bool TryParseRefreshToken(string refreshToken, out int refreshTokenId, out string secret)
    {
        refreshTokenId = default;
        secret = string.Empty;

        var segments = refreshToken.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2 || !int.TryParse(segments[0], out refreshTokenId) || string.IsNullOrWhiteSpace(segments[1]))
        {
            return false;
        }

        secret = segments[1];
        return true;
    }
}
