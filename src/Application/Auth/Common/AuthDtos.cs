using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Common;

public sealed record AuthRoleDto(string Code, string SystemName, string DisplayName);

public sealed record AuthUserDto(
    int Id,
    string? UserCode,
    string FullName,
    DateOnly? DateOfBirth,
    string? PhoneNumber,
    string? Email,
    string? Department,
    UserStatus Status,
    IReadOnlyCollection<AuthRoleDto> Roles);

public sealed record AuthTokensDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record AuthSessionDto(AuthUserDto User, AuthTokensDto Tokens);

public sealed record AuthActionResultDto(string Message);

public sealed record UpdateProfileResultDto(AuthUserDto User, OtpChallengeDto? EmailVerification);

public sealed record OtpChallengeDto(
    int ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt);
