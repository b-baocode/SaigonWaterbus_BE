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

    public static string NormalizeRoleKey(string roleKey)
    {
        Guard.Against.NullOrWhiteSpace(roleKey);
        return roleKey.Trim().ToUpperInvariant();
    }

    public static async Task<IReadOnlyCollection<Role>> GetActiveRolesAsync(
        IApplicationDbContext context,
        int userId,
        CancellationToken cancellationToken)
    {
        var role = await context.Set<User>()
            .Where(x => x.Id == userId)
            .Select(x => x.Role)
            .SingleOrDefaultAsync(cancellationToken);

        return role is null ? [] : [role];
    }

    public static async Task<Role> GetRoleByKeyAsync(
        IApplicationDbContext context,
        string roleKey,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var normalizedRoleKey = NormalizeRoleKey(roleKey);

        return await context.Set<Role>()
            .SingleOrDefaultAsync(
                x => x.SystemName == normalizedRoleKey || x.Code == normalizedRoleKey,
                cancellationToken)
            ?? throw CreateValidationException(propertyName, "Role is invalid.");
    }

    public static async Task<User> GetCurrentUserWithRoleAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.UserId.HasValue)
        {
            throw new UnauthorizedAccessException();
        }

        return await context.Set<User>()
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == userContext.UserId.Value, cancellationToken)
            ?? throw new global::SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Current user was not found.");
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
                user.Department,
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

    public static AuthUserDto CreateUserDto(User user)
    {
        return new AuthUserDto(
            user.Id,
            user.UserCode,
            user.FullName,
            user.DateOfBirth,
            user.PhoneNumber,
            user.Email,
            user.Department,
            user.Status,
            [new AuthRoleDto(user.Role.Code, user.Role.SystemName, user.Role.DisplayName)]);
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
        var currentUser = await GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (!IsAdmin(currentUser))
        {
            throw new ForbiddenAccessException();
        }
    }

    public static async Task<User> EnsureCurrentUserCanManageUsersAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (IsAdmin(currentUser) || IsManager(currentUser))
        {
            return currentUser;
        }

        throw new ForbiddenAccessException();
    }

    public static bool IsAdmin(User user) =>
        string.Equals(user.Role.SystemName, Roles.AdminSystemName, StringComparison.Ordinal);

    public static bool IsManager(User user) =>
        string.Equals(user.Role.SystemName, Roles.ManagerSystemName, StringComparison.Ordinal);

    public static bool IsStaff(User user) =>
        string.Equals(user.Role.SystemName, Roles.StaffSystemName, StringComparison.Ordinal);

    public static bool IsCustomer(User user) =>
        string.Equals(user.Role.SystemName, Roles.CustomerSystemName, StringComparison.Ordinal);

    public static bool RequiresDepartment(Role role) =>
        !string.Equals(role.SystemName, Roles.CustomerSystemName, StringComparison.Ordinal);

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
