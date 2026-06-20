namespace SaigonWaterbus.Infrastructure.Options;

public sealed class OperationScheduleSyncOptions
{
    public const string SectionName = "OperationSchedule";

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 60;

    public int PastDays { get; set; } = 1;

    public int HorizonDays { get; set; } = 63;
}
