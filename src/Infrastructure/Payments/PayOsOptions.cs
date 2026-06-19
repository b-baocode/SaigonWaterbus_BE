namespace SaigonWaterbus.Infrastructure.Payments;

public sealed class PayOsOptions
{
    public const string SectionName = "PayOs";

    public bool Enabled { get; set; }

    public string ApiBaseUrl { get; set; } = "https://api-merchant.payos.vn";

    public string ClientId { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ChecksumKey { get; set; } = string.Empty;

    public string? PartnerCode { get; set; }

    public string ReturnUrl { get; set; } = string.Empty;

    public string CancelUrl { get; set; } = string.Empty;

    public string? PayoutSignatureKey { get; set; }
}
