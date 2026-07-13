namespace SaigonWaterbus.Infrastructure.Media;

public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ApiSecret { get; set; } = string.Empty;

    public string AvatarFolder { get; set; } = "saigon-waterbus/avatars";

    public string BoatFolder { get; set; } = "saigon-waterbus/boats";

    public string BoatDocumentFolder { get; set; } = "saigon-waterbus/boat-documents";

    public string StationFolder { get; set; } = "saigon-waterbus/stations";

    public string BlogFolder { get; set; } = "saigon-waterbus/blog-posts";

    public string PromotionFolder { get; set; } = "saigon-waterbus/promotions";

    public long MaxAvatarBytes { get; set; } = 5 * 1024 * 1024;

    public long MaxBoatImageBytes { get; set; } = 5 * 1024 * 1024;

    public long MaxBoatDocumentBytes { get; set; } = 10 * 1024 * 1024;

    public long MaxStationImageBytes { get; set; } = 5 * 1024 * 1024;

    public long MaxBlogImageBytes { get; set; } = 5 * 1024 * 1024;

    public long MaxPromotionImageBytes { get; set; } = 5 * 1024 * 1024;

    public string[] AllowedAvatarContentTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    public string[] AllowedBoatImageContentTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    public string[] AllowedBoatDocumentContentTypes { get; set; } =
    [
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    public string[] AllowedStationImageContentTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    public string[] AllowedBlogImageContentTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    public string[] AllowedPromotionImageContentTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiSecret);
}
