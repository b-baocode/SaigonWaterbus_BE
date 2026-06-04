namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IVesselImageStorageService
{
    long MaxImageBytes { get; }

    IReadOnlyCollection<string> AllowedImageContentTypes { get; }

    Task<StoredVesselImage> UploadImageAsync(
        VesselImageUpload upload,
        CancellationToken cancellationToken);
}

public sealed record VesselImageUpload(
    int VesselId,
    Stream Content,
    string FileName,
    string? ContentType);

public sealed record StoredVesselImage(
    string Url,
    string PublicId);
