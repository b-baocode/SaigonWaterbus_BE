using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Media;

internal sealed class CloudinaryVesselImageStorageService : IVesselImageStorageService
{
    private readonly CloudinaryOptions _options;

    public CloudinaryVesselImageStorageService(IOptions<CloudinaryOptions> options)
    {
        _options = options.Value;
    }

    public long MaxImageBytes => _options.MaxVesselImageBytes;

    public IReadOnlyCollection<string> AllowedImageContentTypes => _options.AllowedVesselImageContentTypes;

    public async Task<StoredVesselImage> UploadImageAsync(
        VesselImageUpload upload,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        cancellationToken.ThrowIfCancellationRequested();

        var cloudinary = CreateCloudinaryClient();
        var imageId = upload.ImageId ?? Guid.NewGuid();
        var publicId = $"{_options.VesselFolder.Trim('/')}/{upload.VesselId}/{imageId:N}";
        ImageUploadResult result;
        try
        {
            result = await cloudinary.UploadAsync(new ImageUploadParams
            {
                File = new FileDescription(upload.FileName, upload.Content),
                PublicId = publicId,
                Overwrite = true,
                Invalidate = true,
                Transformation = new Transformation()
                    .Width(2000)
                    .Height(1400)
                    .Crop("limit")
                    .Quality("auto:best")
                    .FetchFormat("auto")
            });
        }
        catch (Exception ex) when (ex is not ProfileImageStorageException)
        {
            throw new ProfileImageStorageException($"Unable to upload vessel image to Cloudinary: {ex.Message}", ex);
        }

        if (result.Error is not null)
        {
            throw new ProfileImageStorageException($"Cloudinary vessel image upload failed: {result.Error.Message}");
        }

        if (result.SecureUrl is null || string.IsNullOrWhiteSpace(result.PublicId))
        {
            throw new ProfileImageStorageException("Cloudinary vessel image upload did not return a public URL.");
        }

        return new StoredVesselImage(result.SecureUrl.ToString(), result.PublicId);
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
}
