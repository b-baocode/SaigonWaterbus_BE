using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Boats;

internal static class BoatSupport
{
    public const int MaxBoatImages = 3;

    public static async Task<User> EnsureCurrentUserCanViewBoatsAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(
            context,
            userContext,
            cancellationToken);

        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsManager(actor) || AuthSupport.IsStaff(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static async Task EnsureCurrentUserCanManageBoatsAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(context, userContext, cancellationToken);
    }

    public static bool CanManageBoats(User actor) =>
        AuthSupport.IsAdmin(actor);

    public static IQueryable<Boat> ApplyVisibilityFilter(
        IQueryable<Boat> query,
        User actor)
    {
        return CanManageBoats(actor)
            ? query
            : query.Where(x =>
                x.Status == BoatStatus.Active
                && (x.ServiceType == BoatServiceType.Rescue
                    || x.SeatsConfigured
                    || (x.SeatCount > 0 && x.Seats.Count == x.SeatCount)));
    }

    public static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();

    public static string? NormalizeRegistrationNumber(string? registrationNumber) =>
        string.IsNullOrWhiteSpace(registrationNumber)
            ? null
            : registrationNumber.Trim().ToUpperInvariant();

    public static BoatDto CreateDto(Boat boat, Trip? activeTrip = null, Incident? activeIncident = null)
    {
        var imageUrls = CreateImageUrls(boat);

        return new BoatDto(
            boat.Id,
            boat.Code,
            boat.RegistrationNumber,
            boat.Name,
            boat.ServiceType,
            boat.Status,
            boat.SeatCount,
            boat.NumberOfDecks,
            IsSeatsConfigured(boat),
            IsReadyForOperation(boat),
            boat.MaxSpeedKmh,
            boat.YearBuilt,
            imageUrls.FirstOrDefault() ?? string.Empty,
            imageUrls,
            boat.Description,
            boat.SeatSetupType,
            boat.MaintenanceStartedAt,
            boat.EstimatedMaintenanceEndAt,
            boat.MaintenanceNote,
            BoatDocumentSupport.RequiresDocumentRefresh(boat),
            OperatingStatusSupport.ForBoat(boat, activeTrip, activeIncident));
    }

    public static IReadOnlyCollection<string> CreateImageUrls(Boat boat) =>
        boat.ImageUrls.Length > 0
            ? boat.ImageUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxBoatImages)
                .ToArray()
            : string.IsNullOrWhiteSpace(boat.ImageUrl)
                ? []
                : [boat.ImageUrl.Trim()];

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
            .Take(MaxBoatImages)
            .ToArray();
    }

    public static IReadOnlyCollection<BoatImageFileRequest> CreateImageFiles(
        string? imageFileName,
        string? imageContentType,
        long? imageLength,
        Stream? imageContent,
        IReadOnlyCollection<BoatImageFileRequest>? imageFiles)
    {
        var files = new List<BoatImageFileRequest>();
        if (imageContent is not null)
        {
            files.Add(new BoatImageFileRequest(
                imageFileName ?? string.Empty,
                imageContentType,
                imageLength ?? 0,
                imageContent));
        }

        files.AddRange(imageFiles ?? []);
        return files;
    }

    public static bool IsValidImageUrl(string? imageUrl) =>
        string.IsNullOrWhiteSpace(imageUrl)
        || Uri.TryCreate(imageUrl, UriKind.Absolute, out _);

    public static bool HasValidRequestedImageCount(
        string? imageUrl,
        IReadOnlyCollection<string>? imageUrls,
        Stream? imageContent,
        IReadOnlyCollection<BoatImageFileRequest>? imageFiles)
    {
        var count = CountRequestedImageUrls(imageUrl, imageUrls)
            + CreateImageFiles(null, null, null, imageContent, imageFiles).Count;

        return count <= MaxBoatImages;
    }

    public static void ReplaceImages(
        Boat boat,
        IReadOnlyCollection<string> imageUrls,
        IReadOnlyCollection<StoredBoatImage>? uploadedImages = null)
    {
        var normalizedImageUrls = imageUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxBoatImages)
            .ToArray();

        boat.ImageUrls = normalizedImageUrls;
        boat.ImageUrl = normalizedImageUrls.FirstOrDefault();
        boat.ImagePublicId = null;

        if (uploadedImages is not null
            && uploadedImages.Count == 1
            && normalizedImageUrls.Length == 1
            && string.Equals(normalizedImageUrls[0], uploadedImages.Single().Url, StringComparison.OrdinalIgnoreCase))
        {
            boat.ImagePublicId = uploadedImages.Single().PublicId;
        }
    }

    public static async Task<IReadOnlyCollection<StoredBoatImage>> UploadImagesAsync(
        Guid boatId,
        IReadOnlyCollection<BoatImageFileRequest>? imageFiles,
        IBoatImageStorageService? boatImageStorage,
        string propertyName,
        CancellationToken cancellationToken)
    {
        if (imageFiles is null || imageFiles.Count == 0)
        {
            return [];
        }

        if (boatImageStorage is null)
        {
            throw AuthSupport.CreateValidationException(propertyName, "Dịch vụ lưu ảnh tàu chưa được cấu hình.");
        }

        var uploadedImages = new List<StoredBoatImage>(imageFiles.Count);
        foreach (var file in imageFiles)
        {
            EnsureValidImage(
                propertyName,
                file.FileName,
                file.ContentType,
                file.Length,
                boatImageStorage);

            if (file.Content.CanSeek)
            {
                file.Content.Position = 0;
            }

            uploadedImages.Add(await boatImageStorage.UploadImageAsync(
                new BoatImageUpload(
                    boatId,
                    file.Content,
                    file.FileName,
                    file.ContentType,
                    imageFiles.Count == 1 ? null : Guid.NewGuid()),
                cancellationToken));
        }

        return uploadedImages;
    }

    public static void EnsureValidImage(
        string propertyPrefix,
        string? fileName,
        string? contentType,
        long? length,
        IBoatImageStorageService boatImageStorage)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw AuthSupport.CreateValidationException($"{propertyPrefix}FileName", "Tên file ảnh tàu là bắt buộc.");
        }

        if (!length.HasValue || length <= 0)
        {
            throw AuthSupport.CreateValidationException($"{propertyPrefix}Length", "Ảnh tàu là bắt buộc.");
        }

        if (length > boatImageStorage.MaxImageBytes)
        {
            throw AuthSupport.CreateValidationException(
                $"{propertyPrefix}Length",
                $"Ảnh tàu không được vượt quá {boatImageStorage.MaxImageBytes / 1024 / 1024} MB.");
        }

        if (string.IsNullOrWhiteSpace(contentType)
            || !boatImageStorage.AllowedImageContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw AuthSupport.CreateValidationException(
                $"{propertyPrefix}ContentType",
                "Ảnh tàu chỉ hỗ trợ JPEG, PNG hoặc WebP.");
        }
    }

    public static string? NormalizeOptionalNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant();

    public static int RentalUnitDisplayOrder(BoatRentalUnit rentalUnit) =>
        rentalUnit switch
        {
            BoatRentalUnit.Hour => 1,
            BoatRentalUnit.Day => 2,
            _ => 99
        };

    public static bool IsValidCurrencyCode(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return true;
        }

        var normalizedCurrency = NormalizeCurrency(currency);
        return normalizedCurrency.Length == 3 && normalizedCurrency.All(char.IsAsciiLetterUpper);
    }

    public static bool IsSeatsConfigured(Boat boat) =>
        boat.SeatsConfigured
        || (boat.SeatCount > 0 && boat.Seats.Count == boat.SeatCount);

    public static bool IsReadyForOperation(Boat boat) =>
        boat.Status == BoatStatus.Active
        && IsReadyForActivation(boat);

    public static bool IsReadyForActivation(Boat boat) =>
        boat.ServiceType == BoatServiceType.Rescue
        || (boat.SeatCount > 0 && IsSeatsConfigured(boat));

    public static void EnsureCanActivate(Boat boat, string propertyName)
    {
        if (boat.ServiceType == BoatServiceType.Rescue)
        {
            return;
        }

        if (boat.SeatCount <= 0 || !IsSeatsConfigured(boat))
        {
            throw AuthSupport.CreateValidationException(
                propertyName,
                $"Tàu cần cấu hình đủ {boat.SeatCount} ghế trước khi chuyển Active.");
        }
    }

    private static void AddImageUrl(List<string> urls, string? imageUrl)
    {
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            urls.Add(imageUrl.Trim());
        }
    }

    private static int CountRequestedImageUrls(
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
}
