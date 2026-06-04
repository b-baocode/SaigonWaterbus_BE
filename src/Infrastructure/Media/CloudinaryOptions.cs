namespace SaigonWaterbus.Infrastructure.Media;

public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ApiSecret { get; set; } = string.Empty;

    public string AvatarFolder { get; set; } = "saigon-waterbus/avatars";

    public string VesselFolder { get; set; } = "saigon-waterbus/vessels";

    public long MaxAvatarBytes { get; set; } = 5 * 1024 * 1024;

    public long MaxVesselImageBytes { get; set; } = 5 * 1024 * 1024;

    public string[] AllowedAvatarContentTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    public string[] AllowedVesselImageContentTypes { get; set; } =
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
