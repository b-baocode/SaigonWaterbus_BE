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

    /// <summary>
    /// URL PayOS redirect user về sau khi thanh toán. Phải là URL của endpoint
    /// <c>GET /payment/success</c> trên BE (xem <see cref="SaigonWaterbus.Web.Endpoints.PaymentResults"/>).
    /// VD: https://waterbus.top/payment/success
    /// </summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>
    /// Base URL dùng để build Universal Link cho mobile app (iOS/Android).
    /// Mặc định derive từ <see cref="ReturnUrl"/> (strip path <c>/payment/success</c>).
    /// Universal Link phải trỏ về cùng path mà iOS/Android biết mở app
    /// (đã đăng ký trong apple-app-site-association / assetlinks.json).
    /// VD: https://waterbus.top
    /// </summary>
    public string? ReturnUniversalLinkBase { get; set; }

    public string CancelUrl { get; set; } = string.Empty;

    public string? PayoutClientId { get; set; }

    public string? PayoutApiKey { get; set; }

    public string? PayoutChecksumKey { get; set; }

    public string? PayoutPartnerCode { get; set; }
}
