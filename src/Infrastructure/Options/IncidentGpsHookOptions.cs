namespace SaigonWaterbus.Infrastructure.Options;

public sealed class IncidentGpsHookOptions
{
    public const string SectionName = "IncidentGpsHook";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 5;
}
