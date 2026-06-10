namespace SaigonWaterbus.Infrastructure.Options;

public class DatabaseStartupSettings
{
    public bool ResetOnStartup { get; set; }

    public bool SeedSampleData { get; set; }

    public bool SeedInternalUsers { get; set; }
}
