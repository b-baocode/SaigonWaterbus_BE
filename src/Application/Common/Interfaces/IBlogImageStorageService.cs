namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IBlogImageStorageService
{
    long MaxImageBytes { get; }

    IReadOnlyCollection<string> AllowedImageContentTypes { get; }

    Task<StoredBlogImage> UploadImageAsync(
        BlogImageUpload upload,
        CancellationToken cancellationToken);
}

public sealed record BlogImageUpload(
    Guid BlogPostId,
    Stream Content,
    string FileName,
    string? ContentType,
    Guid? ImageId = null);

public sealed record StoredBlogImage(
    string Url,
    string PublicId);
