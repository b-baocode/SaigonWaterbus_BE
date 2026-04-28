namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class EsmsOptions
{
    public const string SectionName = "Esms";

    public bool Enabled { get; set; }

    public string ApiBaseUrl { get; set; } = "https://rest.esms.vn";

    public string EndpointPath { get; set; } = "/MainService.svc/json/SendMultipleMessage_V4_post_json/";

    public string ApiKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Brandname { get; set; } = "Baotrixemay";

    public string SmsType { get; set; } = "2";

    public string IsUnicode { get; set; } = "0";

    public string Sandbox { get; set; } = "0";

    public string DefaultContent { get; set; } = "123456 la ma xac minh dang ky Baotrixemay cua ban";

    public string? CampaignId { get; set; }

    public string? CallbackUrl { get; set; }
}
