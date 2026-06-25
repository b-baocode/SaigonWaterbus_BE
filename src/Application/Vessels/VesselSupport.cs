using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

internal static class VesselSupport
{
    public const int MaxVesselImages = 1;

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
            : query.Where(x => x.Status == VesselStatus.Active);
    }

    public static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();

    public static string? NormalizeRegistrationNumber(string? registrationNumber) =>
        string.IsNullOrWhiteSpace(registrationNumber)
            ? null
            : registrationNumber.Trim().ToUpperInvariant();

    public static VesselDto CreateDto(Vessel vessel)
    {
        var rentalPrices = CreateRentalPriceDtos(vessel);
        var imageUrls = CreateImageUrls(vessel);

        return new VesselDto(
            vessel.Id,
            vessel.Code,
            vessel.RegistrationNumber,
            vessel.Name,
            vessel.Status,
            vessel.SeatCount,
            vessel.NumberOfDecks,
            IsSeatsConfigured(vessel),
            IsReadyForOperation(vessel),
            vessel.MaxSpeedKmh,
            vessel.YearBuilt,
            imageUrls.FirstOrDefault() ?? string.Empty,
            imageUrls,
            vessel.Description,
            rentalPrices,
            vessel.SeatSetupType);
    }

    public static IReadOnlyCollection<string> CreateImageUrls(Vessel vessel) =>
        string.IsNullOrWhiteSpace(vessel.ImageUrl)
            ? []
            : [vessel.ImageUrl.Trim()];

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
            .Take(MaxVesselImages)
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

    public static void ApplyRentalPrices(
        Vessel vessel,
        IReadOnlyCollection<VesselRentalPriceRequest>? rentalPrices)
    {
        foreach (var request in rentalPrices ?? [])
        {
            vessel.Currency = NormalizeCurrency(request.Currency);
            if (request.RentalUnit == VesselRentalUnit.Hour)
            {
                vessel.HourlyRentalPrice = request.UnitPrice;
            }
            else if (request.RentalUnit == VesselRentalUnit.Day)
            {
                vessel.DailyRentalPrice = request.UnitPrice;
            }
        }
    }

    public static IReadOnlyCollection<VesselRentalPriceDto> CreateRentalPriceDtos(Vessel vessel)
    {
        var prices = new List<VesselRentalPriceDto>();
        if (vessel.HourlyRentalPrice.HasValue)
        {
            prices.Add(new VesselRentalPriceDto(
                VesselRentalUnit.Hour,
                vessel.HourlyRentalPrice.Value,
                vessel.Currency,
                null));
        }

        if (vessel.DailyRentalPrice.HasValue)
        {
            prices.Add(new VesselRentalPriceDto(
                VesselRentalUnit.Day,
                vessel.DailyRentalPrice.Value,
                vessel.Currency,
                null));
        }

        return prices;
    }

    public static string? NormalizeOptionalNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public static bool HasDistinctRentalUnits(IEnumerable<VesselRentalPriceRequest>? rentalPrices) =>
        rentalPrices is null
        || rentalPrices
            .Select(x => x.RentalUnit)
            .Distinct()
            .Count() == rentalPrices.Count();

    public static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant();

    public static int RentalUnitDisplayOrder(VesselRentalUnit rentalUnit) =>
        rentalUnit switch
        {
            VesselRentalUnit.Hour => 1,
            VesselRentalUnit.Day => 2,
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

    public static bool IsSeatsConfigured(Vessel vessel) =>
        vessel.SeatsConfigured
        || (vessel.SeatCount > 0 && vessel.Seats.Count == vessel.SeatCount);

    public static bool IsReadyForOperation(Vessel vessel) =>
        vessel.Status == VesselStatus.Active
        && vessel.SeatCount > 0
        && IsSeatsConfigured(vessel);

    public static void EnsureCanActivate(Vessel vessel, string propertyName)
    {
        if (vessel.SeatCount <= 0 || !IsSeatsConfigured(vessel))
        {
            throw AuthSupport.CreateValidationException(
                propertyName,
                $"Tàu cần cấu hình đủ {vessel.SeatCount} ghế trước khi chuyển Active.");
        }
    }

    private static void AddImageUrl(List<string> urls, string? imageUrl)
    {
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            urls.Add(imageUrl.Trim());
        }
    }
}
