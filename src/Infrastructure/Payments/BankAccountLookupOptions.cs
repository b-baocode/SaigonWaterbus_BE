namespace SaigonWaterbus.Infrastructure.Payments;

public sealed class BankAccountLookupOptions
{
    public const string SectionName = "BankAccountLookup";

    public bool Enabled { get; set; }

    public string Provider { get; set; } = "VietQR";

    public string ApiBaseUrl { get; set; } = "https://api.vietqr.io";

    public string ClientId { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;
}
