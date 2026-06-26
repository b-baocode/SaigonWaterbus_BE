namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IBoatImageStorageService
{
    long MaxImageBytes { get; }

    IReadOnlyCollection<string> AllowedImageContentTypes { get; }

    Task<StoredBoatImage> UploadImageAsync(
        BoatImageUpload upload,
        CancellationToken cancellationToken);
}

public sealed record BoatImageUpload(
    Guid BoatId,
    Stream Content,
    string FileName,
    string? ContentType,
    Guid? ImageId = null);

public sealed record StoredBoatImage(
    string Url,
    string PublicId);
