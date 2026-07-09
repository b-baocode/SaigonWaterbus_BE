using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Media;

internal sealed class CloudinaryBoatDocumentStorageService : IBoatDocumentStorageService
{
    private readonly CloudinaryOptions _options;

    public CloudinaryBoatDocumentStorageService(IOptions<CloudinaryOptions> options)
    {
        _options = options.Value;
    }

    public long MaxDocumentBytes => _options.MaxBoatDocumentBytes;

    public IReadOnlyCollection<string> AllowedDocumentContentTypes => _options.AllowedBoatDocumentContentTypes;

    public async Task<StoredBoatDocument> UploadDocumentAsync(
        BoatDocumentUpload upload,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        cancellationToken.ThrowIfCancellationRequested();

        var cloudinary = CreateCloudinaryClient();
        var extension = SanitizeExtension(Path.GetExtension(upload.FileName));
        var publicId = $"{_options.BoatDocumentFolder.Trim('/')}/{upload.BoatId}/{upload.Type}/{upload.DocumentId:N}{extension}";
        RawUploadResult result;
        try
        {
            result = await cloudinary.UploadAsync(new RawUploadParams
            {
                File = new FileDescription(upload.FileName, upload.Content),
                PublicId = publicId,
                Overwrite = true,
                Invalidate = true,
                UseFilename = false,
                UniqueFilename = false
            });
        }
        catch (Exception ex) when (ex is not ProfileImageStorageException)
        {
            throw new ProfileImageStorageException($"Unable to upload boat document to Cloudinary: {ex.Message}", ex);
        }

        if (result.Error is not null)
        {
            throw new ProfileImageStorageException($"Cloudinary boat document upload failed: {result.Error.Message}");
        }

        if (result.SecureUrl is null || string.IsNullOrWhiteSpace(result.PublicId))
        {
            throw new ProfileImageStorageException("Cloudinary boat document upload did not return a public URL.");
        }

        return new StoredBoatDocument(result.SecureUrl.ToString(), result.PublicId);
    }

    private Cloudinary CreateCloudinaryClient() =>
        new(new Account(
            _options.CloudName,
            _options.ApiKey,
            _options.ApiSecret))
        {
            Api =
            {
                Secure = true
            }
        };

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new ProfileImageStorageException("Cloudinary is not configured.");
        }
    }

    private static string SanitizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var normalizedExtension = extension.Trim().ToLowerInvariant();
        return normalizedExtension.All(character => char.IsAsciiLetterOrDigit(character) || character == '.')
            ? normalizedExtension
            : string.Empty;
    }
}
