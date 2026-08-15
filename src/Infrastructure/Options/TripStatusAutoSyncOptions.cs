namespace SaigonWaterbus.Infrastructure.Options;

public sealed class TripStatusAutoSyncOptions
{
    public const string SectionName = "TripStatusAutoSync";

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 60;
}