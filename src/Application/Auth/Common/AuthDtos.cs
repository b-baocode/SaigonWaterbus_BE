using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Common;

public sealed record AuthRoleDto(string Code, string SystemName, string DisplayName);

public sealed record AuthUserDto(
    int Id,
    string? UserCode,
    string FullName,
    DateOnly? DateOfBirth,
    string? PhoneNumber,
    DateTimeOffset? PhoneVerifiedAt,
    string? Email,
    string? Department,
    string? AvatarUrl,
    AvatarSource AvatarSource,
    UserStatus Status,
    IReadOnlyCollection<AuthRoleDto> Roles);

public sealed record AuthTokensDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record AuthSessionDto(AuthUserDto User, AuthTokensDto Tokens);

public sealed record GoogleLoginResultDto(
    string Status,
    AuthUserDto? User = null,
    AuthTokensDto? Tokens = null,
    string? TempToken = null,
    DateTimeOffset? TempTokenExpiresAt = null);

public sealed record AuthActionResultDto(string Message);

public sealed record UpdateProfileResultDto(AuthUserDto User, OtpChallengeDto? EmailVerification);

public sealed record GooglePhoneOtpSentDto(
    string Status,
    string MaskedPhone,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt);

public sealed record OtpChallengeDto(
    int ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt)
{
    public string MaskedDestination => MaskedEmail;

    public OtpChannel? Channel { get; init; }
}
