using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Common;

internal static class AuthSupport
{
    public const string GoogleProvider = "google";

    public static global::SaigonWaterbus.Application.Common.Exceptions.ValidationException CreateValidationException(string propertyName, string errorMessage) =>
        new([new ValidationFailure(propertyName, errorMessage)]);

    public static OtpChannel ResolveOtpChannel(string? value, OtpChannel defaultChannel, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultChannel;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "email" or "mail" or "e-mail" => OtpChannel.Email,
            "phone" or "sms" or "sdt" or "so-dien-thoai" => OtpChannel.Phone,
            _ => throw CreateValidationException(propertyName, "Kênh OTP phải là email hoặc phone.")
        };
    }

    public static OtpChannel ResolveOtpChannelFromDestination(string destination) =>
        destination.Contains('@', StringComparison.Ordinal)
            ? OtpChannel.Email
            : OtpChannel.Phone;

    public static string NormalizeRoleKey(string roleKey)
    {
        Guard.Against.NullOrWhiteSpace(roleKey);
        return roleKey.Trim().ToUpperInvariant();
    }

    public static async Task<IReadOnlyCollection<Role>> GetActiveRolesAsync(
        IApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var role = await context.Set<User>()
            .Where(x => x.Id == userId)
            .Select(x => x.Role)
            .SingleOrDefaultAsync(cancellationToken);

        return role is null ? [] : [role];
    }

    public static async Task<Role> GetRoleByCodeAsync(
        IApplicationDbContext context,
        string roleCode,
        CancellationToken cancellationToken)
    {
        var normalizedRoleCode = NormalizeRoleKey(roleCode);

        return await context.Set<Role>()
            .SingleOrDefaultAsync(x => x.Code == normalizedRoleCode, cancellationToken)
            ?? throw new InvalidOperationException($"Role '{normalizedRoleCode}' was not found.");
    }

    public static async Task<Role> GetRoleByIdAsync(
        IApplicationDbContext context,
        Guid roleId,
        string propertyName,
        CancellationToken cancellationToken)
    {
        return await context.Set<Role>()
            .SingleOrDefaultAsync(x => x.Id == roleId, cancellationToken)
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

        var user = await context.Set<User>()
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == userContext.UserId.Value, cancellationToken)
            ?? throw new global::SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Current user was not found.");

        EnsureUserCanLogin(user);
        return user;
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
                user.Gender,
                user.Nationality,
                user.PhoneNumber,
                user.PhoneVerifiedAt,
                user.Email,
                user.AvatarUrl,
                user.AvatarSource,
                user.Status,
                roleDtos),
            new AuthTokensDto(
                accessToken.Token,
                accessToken.ExpiresAt,
                refreshToken,
                refreshTokenExpiresAt));
    }

    public static void EnsureUserCanLogin(
        User user,
        string propertyName = "email",
        bool requireVerifiedPhone = true)
    {
        if (user.Status == UserStatus.PendingVerification)
        {
            throw new AccountNotCompletedException(
                AccountNotCompletedException.AccountNotCompletedCode,
                "Tài khoản chưa hoàn tất xác thực OTP.");
        }

        if (user.Status == UserStatus.Suspended)
        {
            throw CreateValidationException(propertyName, "Tài khoản đã bị tạm khóa.");
        }

        if (requireVerifiedPhone
            && !string.IsNullOrWhiteSpace(user.NormalizedPhoneNumber)
            && user.PhoneVerifiedAt is null)
        {
            throw new AccountNotCompletedException(
                AccountNotCompletedException.PhoneNotVerifiedCode,
                "Tài khoản chưa xác minh số điện thoại.");
        }
    }

    public static AuthUserDto CreateUserDto(User user)
    {
        return new AuthUserDto(
            user.Id,
            user.UserCode,
            user.FullName,
            user.DateOfBirth,
            user.Gender,
            user.Nationality,
            user.PhoneNumber,
            user.PhoneVerifiedAt,
            user.Email,
            user.AvatarUrl,
            user.AvatarSource,
            user.Status,
            [new AuthRoleDto(user.Role.Code, user.Role.SystemName, user.Role.DisplayName)]);
    }

    public static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static async Task RevokeActiveRefreshTokensAsync(
        IApplicationDbContext context,
        Guid userId,
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
        Guid userId,
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

    public static async Task<OtpChallenge> ResolveLatestPendingOtpChallengeAsync(
        IApplicationDbContext context,
        OtpChallenge challenge,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        if (challenge.ConsumedAt is null)
        {
            return challenge;
        }

        return await context.Set<OtpChallenge>()
                   .Where(x => x.UserId == challenge.UserId
                            && x.Purpose == purpose
                            && x.ConsumedAt == null)
                   .OrderByDescending(x => x.Id)
                   .FirstOrDefaultAsync(cancellationToken)
               ?? challenge;
    }

    public static async Task<bool> RemoveExpiredPendingRegistrationUsersByIdentityAsync(
        IApplicationDbContext context,
        string? normalizedPhone,
        string? normalizedEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var users = await context.Set<User>()
            .Include(x => x.OtpChallenges)
            .Where(x => x.Status == UserStatus.PendingVerification
                     && ((normalizedPhone != null && x.NormalizedPhoneNumber == normalizedPhone)
                         || (normalizedEmail != null && x.NormalizedEmail == normalizedEmail)))
            .ToListAsync(cancellationToken);

        var removedAny = false;

        foreach (var user in users)
        {
            if (HasUnexpiredRegisterChallenge(user, now))
            {
                continue;
            }

            context.Set<User>().Remove(user);
            removedAny = true;
        }

        return removedAny;
    }

    public static async Task<bool> RemovePendingRegistrationUserIfExpiredAsync(
        IApplicationDbContext context,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var user = await context.Set<User>()
            .Include(x => x.OtpChallenges)
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null || user.Status != UserStatus.PendingVerification)
        {
            return false;
        }

        if (HasUnexpiredRegisterChallenge(user, now))
        {
            return false;
        }

        context.Set<User>().Remove(user);
        return true;
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
        EnsureUserCanLogin(currentUser);

        if (IsAdmin(currentUser) || IsManager(currentUser))
        {
            return currentUser;
        }

        throw new ForbiddenAccessException();
    }

    public static bool IsAdmin(User user) =>
        string.Equals(user.Role.Code, Roles.AdminCode, StringComparison.Ordinal)
        || string.Equals(user.Role.SystemName, Roles.AdminName, StringComparison.Ordinal);

    public static bool IsManager(User user) =>
        string.Equals(user.Role.SystemName, Roles.ManagerSystemName, StringComparison.Ordinal);

    public static bool IsStaff(User user) =>
        string.Equals(user.Role.SystemName, Roles.StaffSystemName, StringComparison.Ordinal);

    public static bool IsCustomer(User user) =>
        string.Equals(user.Role.SystemName, Roles.CustomerSystemName, StringComparison.Ordinal);

    public static string FormatRefreshToken(Guid refreshTokenId, string secret) => $"{refreshTokenId}.{secret}";

    public static bool TryParseRefreshToken(string refreshToken, out Guid refreshTokenId, out string secret)
    {
        refreshTokenId = default;
        secret = string.Empty;

        var segments = refreshToken.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2 || !Guid.TryParse(segments[0], out refreshTokenId) || string.IsNullOrWhiteSpace(segments[1]))
        {
            return false;
        }

        secret = segments[1];
        return true;
    }

    private static bool HasUnexpiredRegisterChallenge(User user, DateTimeOffset now) =>
        user.OtpChallenges.Any(x => x.Purpose == OtpPurpose.Register
                                 && x.ExpiresAt > now);
}
