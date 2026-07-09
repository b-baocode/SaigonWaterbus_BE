using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public sealed class BoatDocument
{
    public Guid Id { get; set; }

    public BoatDocumentType Type { get; set; }

    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long FileSize { get; set; }

    public string FileUrl { get; set; } = null!;

    public string StorageKey { get; set; } = null!;

    public DateOnly? IssuedDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}
