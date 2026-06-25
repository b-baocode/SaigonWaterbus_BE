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
    public const int MaxStationImages = 1;

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
            .Take(MaxStationImages)
            .ToArray();
    }

    public static IReadOnlyCollection<string> CreateImageUrls(Station station) =>
        string.IsNullOrWhiteSpace(station.ImageUrl)
            ? []
            : [station.ImageUrl.Trim()];

    public static bool HasValidRequestedImageCount(
        string? imageUrl,
        IReadOnlyCollection<string>? imageUrls,
        IReadOnlyCollection<StationImageFileRequest>? imageFiles = null) =>
        CountRequestedImages(imageUrl, imageUrls) + (imageFiles?.Count ?? 0) <= MaxStationImages;

    private static int CountRequestedImages(
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
            .Count();
    }

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

    public static void ReplaceImages(Station station, IReadOnlyCollection<string> imageUrls)
    {
        station.ImageUrl = imageUrls.FirstOrDefault();
        station.ImagePublicId = null;
    }

    private static void AddImageUrl(List<string> urls, string? imageUrl)
    {
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            urls.Add(imageUrl.Trim());
        }
    }
}
