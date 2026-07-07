namespace SaigonWaterbus.Infrastructure.Options;

public sealed class CharterBookingExpirationOptions
{
    public const string SectionName = "CharterBookingExpiration";

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 60;
}
