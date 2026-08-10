namespace SaigonWaterbus.Infrastructure.Push;

public sealed class ExpoPushOptions
{
    public const string SectionName = "ExpoPush";

    public string Endpoint { get; set; } = "https://exp.host/--/api/v2/push/send";

    public string? AccessToken { get; set; }

    public int MaxRetries { get; set; } = 3;

    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public bool AutoSendOnNotification { get; set; } = true;
}