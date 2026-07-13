namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IPromotionImageStorageService
{
    long MaxImageBytes { get; }

    IReadOnlyCollection<string> AllowedImageContentTypes { get; }

    Task<StoredPromotionImage> UploadImageAsync(
        PromotionImageUpload upload,
        CancellationToken cancellationToken);
}

public sealed record PromotionImageUpload(
    Guid PromotionId,
    Stream Content,
    string FileName,
    string? ContentType,
    Guid? ImageId = null);

public sealed record StoredPromotionImage(
    string Url,
    string PublicId);
