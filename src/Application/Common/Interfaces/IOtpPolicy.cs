namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IOtpPolicy
{
    int ExpirationMinutes { get; }

    int ResendSeconds { get; }

    int MaxAttempts { get; }
}
