namespace SaigonWaterbus.Application.Common.Interfaces;

public sealed record GoogleLoginTempSession(
    string TempToken,
    int? ExistingUserId,
    string GoogleUserId,
    string Email,
    string NormalizedEmail,
    string? Name,
    string? AvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? PhoneNumber = null,
    string? NormalizedPhoneNumber = null,
    string? OtpCodeHash = null,
    DateTimeOffset? OtpExpiresAt = null,
    DateTimeOffset? OtpResendAvailableAt = null,
    int OtpAttemptCount = 0,
    int OtpMaxAttempts = 0);

public interface IGoogleLoginTempStore
{
    Task SaveAsync(GoogleLoginTempSession session, CancellationToken cancellationToken);

    Task<GoogleLoginTempSession?> GetAsync(string tempToken, CancellationToken cancellationToken);

    Task RemoveAsync(string tempToken, CancellationToken cancellationToken);
}
