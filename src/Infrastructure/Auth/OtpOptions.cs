namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    public int CodeLength { get; set; } = 6;

    public int ExpirationMinutes { get; set; } = 5;

    public int ResendSeconds { get; set; } = 60;

    public int MaxAttempts { get; set; } = 5;
}
