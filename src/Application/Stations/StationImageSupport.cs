using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Stations;

public sealed record StationImageFileRequest(
    string FileName,
    string? ContentType,
    long Length,
    Stream Content);

internal static class StationImageSupport
{
    public const int MaxStationImages = 6;

    public static IReadOnlyCollection<string> NormalizeImageUrls(
        string? imageUrl,
        IReadOnlyCollection<string>? imageUrls)
    {
        var urls = new List<string>();
        AddImageUrl(urls, imageUrl);

        foreach (var url in imageUrls ?? [])
        {
            AddImageUrl(urls, url);
        }

        return urls
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyCollection<string> CreateImageUrls(Station station)
    {
        var urls = station.Images
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .Select(x => x.Url.Trim())
            .ToList();

        if (!string.IsNullOrWhiteSpace(station.ImageUrl)
            && !urls.Contains(station.ImageUrl.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            urls.Insert(0, station.ImageUrl.Trim());
        }

        return urls
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool HasValidRequestedImageCount(
        string? imageUrl,
        IReadOnlyCollection<string>? imageUrls,
        IReadOnlyCollection<StationImageFileRequest>? imageFiles = null) =>
        NormalizeImageUrls(imageUrl, imageUrls).Count + (imageFiles?.Count ?? 0) <= MaxStationImages;

    public static void EnsureValidImage(
        string propertyPrefix,
        string? fileName,
        string? contentType,
        long? length,
        IStationImageStorageService stationImageStorage)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw AuthSupport.CreateValidationException($"{propertyPrefix}FileName", "Tên file ảnh trạm là bắt buộc.");
        }

        if (!length.HasValue || length <= 0)
        {
            throw AuthSupport.CreateValidationException($"{propertyPrefix}Length", "Ảnh trạm là bắt buộc.");
        }

        if (length > stationImageStorage.MaxImageBytes)
        {
            throw AuthSupport.CreateValidationException(
                $"{propertyPrefix}Length",
                $"Ảnh trạm không được vượt quá {stationImageStorage.MaxImageBytes / 1024 / 1024} MB.");
        }

        if (string.IsNullOrWhiteSpace(contentType)
            || !stationImageStorage.AllowedImageContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw AuthSupport.CreateValidationException(
                $"{propertyPrefix}ContentType",
                "Ảnh trạm chỉ hỗ trợ JPEG, PNG hoặc WebP.");
        }
    }

    public static bool IsValidImageUrl(string? imageUrl) =>
        string.IsNullOrWhiteSpace(imageUrl)
        || Uri.TryCreate(imageUrl, UriKind.Absolute, out _);

    public static StationImage CreateImage(
        Guid stationId,
        string url,
        int displayOrder,
        bool isPrimary) =>
        new()
        {
            StationId = stationId,
            Url = url,
            DisplayOrder = displayOrder,
            IsPrimary = isPrimary
        };

    public static void ReplaceImages(Station station, IReadOnlyCollection<string> imageUrls)
    {
        station.Images = new List<StationImage>();
        station.ImageUrl = null;
        station.ImagePublicId = null;

        var displayOrder = 1;
        foreach (var imageUrl in imageUrls)
        {
            station.Images.Add(CreateImage(
                station.Id,
                imageUrl,
                displayOrder++,
                isPrimary: station.Images.Count == 0));
        }

        SyncPrimaryImage(station);
    }

    public static void SyncPrimaryImage(Station station)
    {
        var orderedImages = station.Images
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .ToArray();

        if (orderedImages.Length == 0)
        {
            station.ImageUrl = null;
            station.ImagePublicId = null;
            return;
        }

        var primary = orderedImages.FirstOrDefault(x => x.IsPrimary) ?? orderedImages[0];
        foreach (var image in orderedImages)
        {
            image.IsPrimary = image.Id == primary.Id;
        }

        station.ImageUrl = primary.Url;
        station.ImagePublicId = primary.PublicId;
    }

    private static void AddImageUrl(List<string> urls, string? imageUrl)
    {
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            urls.Add(imageUrl.Trim());
        }
    }
}
