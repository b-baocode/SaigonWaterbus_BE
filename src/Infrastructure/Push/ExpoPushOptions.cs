namespace SaigonWaterbus.Infrastructure.Push;

public sealed class ExpoPushOptions
{
    public const string SectionName = "ExpoPush";

    /// <summary>
    /// Expo Push endpoint. Default: https://exp.host/--/api/v2/push/send
    /// </summary>
    public string Endpoint { get; set; } = "https://exp.host/--/api/v2/push/send";

    /// <summary>
    /// Optional Expo Access Token for authenticated push (tăng rate limit).
    /// Nếu rỗng → gửi anonymous.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Số request tối đa retry khi gặp transient error (5xx, network).
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Timeout cho mỗi HTTP request.
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Tự động gửi push khi notification được save.
    /// Khi false, FE phải subscribe push channel riêng.
    /// </summary>
    public bool AutoSendOnNotification { get; set; } = true;
}
