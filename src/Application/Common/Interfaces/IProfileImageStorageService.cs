namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IProfileImageStorageService
{
    long MaxAvatarBytes { get; }

    IReadOnlyCollection<string> AllowedAvatarContentTypes { get; }

    Task<StoredProfileImage> UploadAvatarAsync(
        ProfileImageUpload upload,
        CancellationToken cancellationToken);

    Task<StoredProfileImage> ImportAvatarFromUrlAsync(
        ProfileImageUrlImport import,
        CancellationToken cancellationToken);
}

public sealed record ProfileImageUpload(
    Guid UserId,
    Stream Content,
    string FileName,
    string? ContentType);

public sealed record ProfileImageUrlImport(
    Guid UserId,
    string ImageUrl,
    string FileName);

public sealed record StoredProfileImage(
    string Url,
    string PublicId);
