namespace SaigonWaterbus.Infrastructure.Options;

public class DatabaseStartupSettings
{
    public bool ApplyMigrationsOnStartup { get; set; }

    public bool ResetOnStartup { get; set; }

    public bool SeedSampleData { get; set; }

    public bool SeedInternalUsers { get; set; }
}
