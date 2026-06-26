namespace SaigonWaterbus.Infrastructure.Options;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public bool Enabled { get; set; }

    public string ConnectionString { get; set; } = string.Empty;

    public string InstanceName { get; set; } = "saigon-waterbus:";

    public int DefaultTtlMinutes { get; set; } = 15;

    public int OtpTtlMinutes { get; set; } = 5;

    public int BoatHoldTtlMinutes { get; set; } = 15;

    public int PaymentStatusTtlMinutes { get; set; } = 20;

    public int LockTtlSeconds { get; set; } = 60;
}
