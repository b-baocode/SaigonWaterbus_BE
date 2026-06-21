namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IStationImageStorageService
{
    long MaxImageBytes { get; }

    IReadOnlyCollection<string> AllowedImageContentTypes { get; }

    Task<StoredStationImage> UploadImageAsync(
        StationImageUpload upload,
        CancellationToken cancellationToken);
}

public sealed record StationImageUpload(
    Guid StationId,
    Stream Content,
    string FileName,
    string? ContentType,
    Guid? ImageId = null);

public sealed record StoredStationImage(
    string Url,
    string PublicId);
