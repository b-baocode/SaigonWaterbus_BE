using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Media;

internal sealed class CloudinaryInsurancePackageImageStorageService : IInsurancePackageImageStorageService
{
    private readonly CloudinaryOptions _options;

    public CloudinaryInsurancePackageImageStorageService(IOptions<CloudinaryOptions> options)
    {
        _options = options.Value;
    }

    public long MaxImageBytes => _options.MaxInsurancePackageImageBytes;

    public IReadOnlyCollection<string> AllowedImageContentTypes => _options.AllowedInsurancePackageImageContentTypes;

    public async Task<StoredInsurancePackageImage> UploadImageAsync(
        InsurancePackageImageUpload upload,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        cancellationToken.ThrowIfCancellationRequested();

        var cloudinary = new Cloudinary(new Account(
            _options.CloudName,
            _options.ApiKey,
            _options.ApiSecret))
        {
            Api = { Secure = true }
        };
        var publicId = $"{_options.InsurancePackageFolder.Trim('/')}/{upload.InsurancePackageId}/{(upload.ImageId ?? Guid.NewGuid()):N}";
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
                    .Width(1200)
                    .Height(1200)
                    .Crop("limit")
                    .Quality("auto:best")
                    .FetchFormat("auto")
            });
        }
        catch (Exception ex) when (ex is not ProfileImageStorageException)
        {
            throw new ProfileImageStorageException($"Unable to upload insurance package image to Cloudinary: {ex.Message}", ex);
        }

        if (result.Error is not null)
        {
            throw new ProfileImageStorageException($"Cloudinary insurance package image upload failed: {result.Error.Message}");
        }

        if (result.SecureUrl is null || string.IsNullOrWhiteSpace(result.PublicId))
        {
            throw new ProfileImageStorageException("Cloudinary insurance package image upload did not return a public URL.");
        }

        return new StoredInsurancePackageImage(result.SecureUrl.ToString(), result.PublicId);
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new ProfileImageStorageException("Cloudinary is not configured.");
        }
    }
}
