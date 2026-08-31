namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IInsurancePackageImageStorageService
{
    long MaxImageBytes { get; }

    IReadOnlyCollection<string> AllowedImageContentTypes { get; }

    Task<StoredInsurancePackageImage> UploadImageAsync(
        InsurancePackageImageUpload upload,
        CancellationToken cancellationToken);
}

public sealed record InsurancePackageImageUpload(
    Guid InsurancePackageId,
    Stream Content,
    string FileName,
    string? ContentType,
    Guid? ImageId = null);

public sealed record StoredInsurancePackageImage(string Url, string PublicId);
