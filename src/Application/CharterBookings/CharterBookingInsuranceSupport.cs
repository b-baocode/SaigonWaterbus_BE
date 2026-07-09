using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingInsuranceSupport
{
    public static async Task<BookingInsuranceSnapshot?> CreateSelectedInsuranceSnapshotAsync(
        IApplicationDbContext context,
        Booking booking,
        Guid? insurancePackageId,
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

        var quantity = ResolvePassengerQuantity(booking);
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
            Quantity = quantity,
            TotalAmount = package.UnitPremiumAmount * quantity,
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

    private static int ResolvePassengerQuantity(Booking booking)
    {
        var passengerCount = booking.PassengerCount.GetValueOrDefault();
        if (passengerCount > 0)
        {
            return passengerCount;
        }

        return booking.AdultCount.GetValueOrDefault() + booking.ChildCount.GetValueOrDefault();
    }

    private static ValidationException CreateInsuranceValidation(string message) =>
        new([new ValidationFailure("insurancePackageId", message)]);
}
