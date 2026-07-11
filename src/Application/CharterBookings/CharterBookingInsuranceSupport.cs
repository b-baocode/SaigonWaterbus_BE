using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingInsuranceSupport
{
    public static async Task<BookingInsuranceSnapshot?> ResolveRequestedInsuranceSnapshotAsync(
        IApplicationDbContext context,
        bool? insuranceSelected,
        Guid? insurancePackageId,
        BookingInsuranceSnapshot? currentSnapshot,
        int chargeableSeatQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken)
    {
        if (insuranceSelected == false)
        {
            return null;
        }

        if (insuranceSelected == true)
        {
            if (!insurancePackageId.HasValue)
            {
                throw CreateInsuranceValidation("Vui lòng chọn gói bảo hiểm.");
            }

            return await CreateSelectedInsuranceSnapshotAsync(
                context,
                insurancePackageId,
                chargeableSeatQuantity,
                quotedAt,
                cancellationToken);
        }

        if (insurancePackageId.HasValue)
        {
            return await CreateSelectedInsuranceSnapshotAsync(
                context,
                insurancePackageId,
                chargeableSeatQuantity,
                quotedAt,
                cancellationToken);
        }

        return currentSnapshot is null
            ? null
            : UpdateQuantity(currentSnapshot, chargeableSeatQuantity, quotedAt);
    }

    public static async Task<BookingInsuranceSnapshot?> CreateSelectedInsuranceSnapshotAsync(
        IApplicationDbContext context,
        Guid? insurancePackageId,
        int chargeableSeatQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken)
    {
        if (!insurancePackageId.HasValue)
        {
            return null;
        }

        var package = await context.Set<InsurancePackage>()
            .AsNoTracking()
            .Where(x => x.Id == insurancePackageId.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (package is null)
        {
            throw CreateInsuranceValidation("Không tìm thấy gói bảo hiểm đã chọn.");
        }

        if (!string.Equals(package.BookingType, Booking.CharterBookingType, StringComparison.Ordinal)
            || !package.IsActive)
        {
            throw CreateInsuranceValidation("Gói bảo hiểm đã chọn không khả dụng cho charter booking.");
        }

        if (chargeableSeatQuantity <= 0)
        {
            throw CreateInsuranceValidation("Không xác định được số ghế tính bảo hiểm cho booking thuê tàu.");
        }

        return new BookingInsuranceSnapshot
        {
            InsurancePackageId = package.Id,
            Code = package.Code,
            Name = package.Name,
            BookingType = package.BookingType,
            IsRequired = package.IsRequired,
            ProviderName = package.ProviderName,
            ProviderLogoUrl = package.ProviderLogoUrl,
            UnitPremiumAmount = package.UnitPremiumAmount,
            CoverageAmount = package.CoverageAmount,
            Currency = package.Currency,
            Conditions = package.Conditions,
            TermsUrl = package.TermsUrl,
            Quantity = chargeableSeatQuantity,
            TotalAmount = package.UnitPremiumAmount * chargeableSeatQuantity,
            QuotedAt = quotedAt
        };
    }

    public static CharterBookingInsuranceDto? ToDto(BookingInsuranceSnapshot? snapshot) =>
        snapshot is null
            ? null
            : new CharterBookingInsuranceDto(
                snapshot.InsurancePackageId,
                snapshot.Code,
                snapshot.Name,
                snapshot.BookingType,
                snapshot.IsRequired,
                snapshot.ProviderName,
                snapshot.ProviderLogoUrl,
                snapshot.UnitPremiumAmount,
                snapshot.CoverageAmount,
                snapshot.Currency,
                snapshot.Quantity,
                snapshot.TotalAmount,
                snapshot.Conditions,
                snapshot.TermsUrl,
                snapshot.QuotedAt);

    private static BookingInsuranceSnapshot UpdateQuantity(
        BookingInsuranceSnapshot snapshot,
        int chargeableSeatQuantity,
        DateTimeOffset quotedAt)
    {
        if (chargeableSeatQuantity <= 0)
        {
            throw CreateInsuranceValidation("Không xác định được số ghế tính bảo hiểm cho booking thuê tàu.");
        }

        snapshot.Quantity = chargeableSeatQuantity;
        snapshot.TotalAmount = snapshot.UnitPremiumAmount * chargeableSeatQuantity;
        snapshot.QuotedAt = quotedAt;
        return snapshot;
    }

    private static ValidationException CreateInsuranceValidation(string message) =>
        new([new ValidationFailure("insurancePackageId", message)]);
}
