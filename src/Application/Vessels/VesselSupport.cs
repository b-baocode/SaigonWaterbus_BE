using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

internal static class VesselSupport
{
    public const int MaxVesselImages = 10;

    public static async Task<User> EnsureCurrentUserCanViewVesselsAsync(
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

    public static async Task EnsureCurrentUserCanManageVesselsAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(context, userContext, cancellationToken);
    }

    public static bool CanManageVessels(User actor) =>
        AuthSupport.IsAdmin(actor);

    public static IQueryable<Vessel> ApplyVisibilityFilter(
        IQueryable<Vessel> query,
        User actor)
    {
        return CanManageVessels(actor)
            ? query
            : query.Where(x =>
                x.Status == VesselStatus.Active
                && x.SeatsConfigured);
    }

    public static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();

    public static string? NormalizeRegistrationNumber(string? registrationNumber) =>
        string.IsNullOrWhiteSpace(registrationNumber)
            ? null
            : registrationNumber.Trim().ToUpperInvariant();

    public static VesselDto CreateDto(Vessel vessel)
    {
        var rentalPrices = vessel.RentalPrices
            .OrderBy(x => RentalUnitDisplayOrder(x.RentalUnit))
            .ThenBy(x => x.Id)
            .Select(x => new VesselRentalPriceDto(x.RentalUnit, x.UnitPrice, x.Currency, x.Note))
            .ToArray();
        var rentalPrice = rentalPrices.FirstOrDefault(x => x.RentalUnit == VesselRentalUnit.Day)
            ?? rentalPrices.FirstOrDefault();
        var imageUrls = CreateImageUrls(vessel);
        var primaryImageUrl = imageUrls.FirstOrDefault() ?? string.Empty;

        return new VesselDto(
            vessel.Id,
            vessel.Code,
            vessel.RegistrationNumber,
            vessel.Name,
            vessel.Status,
            vessel.SeatCount,
            vessel.NumberOfDecks,
            vessel.SeatsConfigured,
            IsReadyForOperation(vessel),
            vessel.MaxSpeedKmh,
            vessel.YearBuilt,
            primaryImageUrl,
            imageUrls,
            vessel.Description,
            rentalPrice,
            rentalPrices,
            vessel.SeatSetupType);
    }

    public static IReadOnlyCollection<string> CreateImageUrls(Vessel vessel)
    {
        var urls = vessel.Images
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .Select(x => x.Url.Trim())
            .ToList();

        if (!string.IsNullOrWhiteSpace(vessel.ImageUrl)
            && !urls.Contains(vessel.ImageUrl.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            urls.Insert(0, vessel.ImageUrl.Trim());
        }

        return urls
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

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

    public static IReadOnlyCollection<VesselImageFileRequest> CreateImageFiles(
        string? imageFileName,
        string? imageContentType,
        long? imageLength,
        Stream? imageContent,
        IReadOnlyCollection<VesselImageFileRequest>? imageFiles)
    {
        var files = new List<VesselImageFileRequest>();
        if (imageContent is not null)
        {
            files.Add(new VesselImageFileRequest(
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
        IReadOnlyCollection<VesselImageFileRequest>? imageFiles)
    {
        var count = NormalizeImageUrls(imageUrl, imageUrls).Count
            + CreateImageFiles(null, null, null, imageContent, imageFiles).Count;

        return count <= MaxVesselImages;
    }

    public static VesselImage CreateImage(
        string url,
        string? publicId,
        int displayOrder,
        bool isPrimary) =>
        new()
        {
            Url = url,
            PublicId = publicId,
            DisplayOrder = displayOrder,
            IsPrimary = isPrimary
        };

    public static void SyncPrimaryImage(Vessel vessel)
    {
        var orderedImages = vessel.Images
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .ToArray();

        if (orderedImages.Length == 0)
        {
            vessel.ImageUrl = null;
            vessel.ImagePublicId = null;
            return;
        }

        var primary = orderedImages.FirstOrDefault(x => x.IsPrimary) ?? orderedImages[0];
        foreach (var image in orderedImages)
        {
            image.IsPrimary = image.Id == primary.Id;
        }

        vessel.ImageUrl = primary.Url;
        vessel.ImagePublicId = primary.PublicId;
    }

    public static int NextImageDisplayOrder(Vessel vessel) =>
        vessel.Images.Count == 0
            ? 1
            : vessel.Images.Max(x => x.DisplayOrder) + 1;

    private static void AddImageUrl(List<string> urls, string? imageUrl)
    {
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            urls.Add(imageUrl.Trim());
        }
    }

    public static VesselRentalPrice CreateRentalPrice(Guid vesselId, VesselRentalPriceRequest request) =>
        new()
        {
            VesselId = vesselId,
            RentalUnit = request.RentalUnit,
            UnitPrice = request.UnitPrice,
            Currency = NormalizeCurrency(request.Currency),
            Note = NormalizeOptionalNote(request.Note)
        };

    public static string? NormalizeOptionalNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public static bool HasDistinctRentalUnits(IEnumerable<VesselRentalPriceRequest>? rentalPrices) =>
        rentalPrices is null
        || rentalPrices
            .Select(x => x.RentalUnit)
            .Distinct()
            .Count() == rentalPrices.Count();

    public static int RentalUnitDisplayOrder(VesselRentalUnit rentalUnit) =>
        rentalUnit switch
        {
            VesselRentalUnit.Hour => 1,
            VesselRentalUnit.Day => 2,
            _ => 99
        };

    public static IReadOnlyCollection<string> RequiredSeatTypeCodes(SeatSetupType seatSetupType) =>
        seatSetupType switch
        {
            _ => ["STANDARD"]
        };

    public static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant();

    public static bool IsValidCurrencyCode(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return true;
        }

        var normalizedCurrency = NormalizeCurrency(currency);
        return normalizedCurrency.Length == 3 && normalizedCurrency.All(char.IsAsciiLetterUpper);
    }

    public static bool IsReadyForOperation(Vessel vessel) =>
        vessel.Status == VesselStatus.Active
        && vessel.SeatsConfigured;

    public static void EnsureCanActivate(Vessel vessel, string propertyName)
    {
        if (!vessel.SeatsConfigured)
        {
            throw AuthSupport.CreateValidationException(
                propertyName,
                "Tàu phải setup đủ ghế trước khi chuyển Active.");
        }
    }

    public static void EnsureValidImage(
        string propertyPrefix,
        string? fileName,
        string? contentType,
        long? length,
        IVesselImageStorageService vesselImageStorage)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw AuthSupport.CreateValidationException($"{propertyPrefix}FileName", "Tên file ảnh tàu là bắt buộc.");
        }

        if (!length.HasValue || length <= 0)
        {
            throw AuthSupport.CreateValidationException($"{propertyPrefix}Length", "Ảnh tàu là bắt buộc.");
        }

        if (length > vesselImageStorage.MaxImageBytes)
        {
            throw AuthSupport.CreateValidationException(
                $"{propertyPrefix}Length",
                $"Ảnh tàu không được vượt quá {vesselImageStorage.MaxImageBytes / 1024 / 1024} MB.");
        }

        if (string.IsNullOrWhiteSpace(contentType)
            || !vesselImageStorage.AllowedImageContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw AuthSupport.CreateValidationException(
                $"{propertyPrefix}ContentType",
                "Ảnh tàu chỉ hỗ trợ JPEG, PNG hoặc WebP.");
        }
    }
}
