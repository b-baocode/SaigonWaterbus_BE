using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

internal static class VesselSupport
{
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

    public static VesselDto CreateDto(Vessel vessel, int generatedSeatCount = 0)
    {
        var rentalPrices = vessel.RentalPrices
            .OrderBy(x => RentalUnitDisplayOrder(x.RentalUnit))
            .ThenBy(x => x.Id)
            .Select(x => new VesselRentalPriceDto(x.RentalUnit, x.UnitPrice, x.Currency, x.Note))
            .ToArray();
        var rentalPrice = rentalPrices.FirstOrDefault(x => x.RentalUnit == VesselRentalUnit.Day)
            ?? rentalPrices.FirstOrDefault();

        return new VesselDto(
            vessel.Id,
            vessel.Code,
            vessel.RegistrationNumber,
            vessel.Name,
            vessel.Status,
            vessel.SeatCount,
            generatedSeatCount,
            vessel.NumberOfDecks,
            vessel.SeatsConfigured,
            IsReadyForOperation(vessel),
            vessel.MaxSpeedKmh,
            vessel.YearBuilt,
            vessel.ImageUrl ?? string.Empty,
            vessel.Description,
            rentalPrice,
            rentalPrices,
            vessel.SeatSetupType);
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
        seatSetupType == SeatSetupType.StandardAndVip
            ? ["STANDARD", "VIP"]
            : ["STANDARD"];

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
