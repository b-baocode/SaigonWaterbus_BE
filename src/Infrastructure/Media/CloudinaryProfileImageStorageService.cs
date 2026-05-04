using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Media;

internal sealed class CloudinaryProfileImageStorageService : IProfileImageStorageService
{
    private readonly CloudinaryOptions _options;

    public CloudinaryProfileImageStorageService(IOptions<CloudinaryOptions> options)
    {
        _options = options.Value;
    }

    public long MaxAvatarBytes => _options.MaxAvatarBytes;

    public IReadOnlyCollection<string> AllowedAvatarContentTypes => _options.AllowedAvatarContentTypes;

    public async Task<StoredProfileImage> UploadAvatarAsync(
        ProfileImageUpload upload,
        CancellationToken cancellationToken)
    {
        return await UploadAvatarFileAsync(
            upload.UserId,
            new FileDescription(upload.FileName, upload.Content),
            cancellationToken);
    }

    public async Task<StoredProfileImage> ImportAvatarFromUrlAsync(
        ProfileImageUrlImport import,
        CancellationToken cancellationToken)
    {
        return await UploadAvatarFileAsync(
            import.UserId,
            new FileDescription(import.FileName, import.ImageUrl),
            cancellationToken);
    }

    private async Task<StoredProfileImage> UploadAvatarFileAsync(
        int userId,
        FileDescription file,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        cancellationToken.ThrowIfCancellationRequested();

        var cloudinary = CreateCloudinaryClient();
        var publicId = $"{_options.AvatarFolder.Trim('/')}/users/{userId}";
        ImageUploadResult result;
        try
        {
            result = await cloudinary.UploadAsync(new ImageUploadParams
            {
                File = file,
                PublicId = publicId,
                Overwrite = true,
                Invalidate = true,
                Transformation = new Transformation()
                    .Width(512)
                    .Height(512)
                    .Crop("fill")
                    .Gravity("face")
            });
        }
        catch (Exception ex) when (ex is not ProfileImageStorageException)
        {
            throw new ProfileImageStorageException($"Unable to upload avatar to Cloudinary: {ex.Message}");
        }

        if (result.Error is not null)
        {
            throw new ProfileImageStorageException($"Cloudinary avatar upload failed: {result.Error.Message}");
        }

        if (result.SecureUrl is null || string.IsNullOrWhiteSpace(result.PublicId))
        {
            throw new ProfileImageStorageException("Cloudinary avatar upload did not return a public URL.");
        }

        return new StoredProfileImage(result.SecureUrl.ToString(), result.PublicId);
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
