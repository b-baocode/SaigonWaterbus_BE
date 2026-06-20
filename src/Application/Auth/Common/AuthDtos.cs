using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Common;

public sealed record AuthRoleDto(string Code, string SystemName, string DisplayName);

public sealed record AuthStationAssignmentDto(
    Guid StationId,
    string StationCode,
    string StationName,
    bool IsPrimary,
    bool IsActive);

public sealed record AuthUserDto(
    Guid Id,
    string? Code,
    string FullName,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Nationality,
    string? PhoneNumber,
    DateTimeOffset? PhoneVerifiedAt,
    string? Email,
    string? AvatarUrl,
    AvatarSource AvatarSource,
    UserStatus Status,
    IReadOnlyCollection<AuthRoleDto> Roles,
    IReadOnlyCollection<AuthStationAssignmentDto> StationAssignments);

public sealed record AuthTokensDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record AuthSessionDto(AuthUserDto User, AuthTokensDto Tokens);

public sealed record GoogleLoginResultDto(
    string Status,
    AuthUserDto? User = null,
    AuthTokensDto? Tokens = null);

public sealed record AuthActionResultDto(string Message);

public sealed record UpdateProfileResultDto(
    AuthUserDto User,
    OtpChallengeDto? EmailVerification = null,
    OtpChallengeDto? PhoneVerification = null);

public sealed record OtpChallengeDto(
    Guid Id,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt)
{
    public string MaskedDestination => MaskedEmail;

    public OtpChannel? Channel { get; init; }
}
