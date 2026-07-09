using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IBoatDocumentStorageService
{
    long MaxDocumentBytes { get; }

    IReadOnlyCollection<string> AllowedDocumentContentTypes { get; }

    Task<StoredBoatDocument> UploadDocumentAsync(
        BoatDocumentUpload upload,
        CancellationToken cancellationToken);

    string CreateDocumentUrl(string storageKey);
}

public sealed record BoatDocumentUpload(
    Guid BoatId,
    BoatDocumentType Type,
    Guid DocumentId,
    Stream Content,
    string FileName,
    string? ContentType);

public sealed record StoredBoatDocument(
    string Url,
    string StorageKey);
