using FluentValidation.Results;
using SaigonWaterbus.Application.Payments;
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
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken,
        string bookingType = Booking.CharterBookingType)
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
                insuredPassengerQuantity,
                quotedAt,
                cancellationToken,
                bookingType);
        }

        if (insurancePackageId.HasValue)
        {
            return await CreateSelectedInsuranceSnapshotAsync(
                context,
                insurancePackageId,
                insuredPassengerQuantity,
                quotedAt,
                cancellationToken,
                bookingType);
        }

        return currentSnapshot is null
            ? null
            : UpdateQuantity(currentSnapshot, insuredPassengerQuantity, quotedAt);
    }

    public static async Task<BookingInsuranceSnapshot?> ResolveSeatBookingInsuranceSnapshotAsync(
        IApplicationDbContext context,
        bool? insuranceSelected,
        Guid? insurancePackageId,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken)
    {
        if (insuranceSelected == false)
        {
            return null;
        }

        if (insurancePackageId.HasValue || insuranceSelected == true)
        {
            if (insuranceSelected == true && !insurancePackageId.HasValue)
            {
                return await ResolveDefaultInsuranceSnapshotAsync(
                    context,
                    Booking.SeatBookingType,
                    insuredPassengerQuantity,
                    quotedAt,
                    cancellationToken);
            }

            return await CreateSelectedInsuranceSnapshotAsync(
                context,
                insurancePackageId,
                insuredPassengerQuantity,
                quotedAt,
                cancellationToken,
                Booking.SeatBookingType);
        }

        return await ResolveDefaultInsuranceSnapshotAsync(
            context,
            Booking.SeatBookingType,
            insuredPassengerQuantity,
            quotedAt,
            cancellationToken);
    }

    public static async Task<BookingInsuranceSnapshot?> CreateSelectedInsuranceSnapshotAsync(
        IApplicationDbContext context,
        Guid? insurancePackageId,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken,
        string bookingType = Booking.CharterBookingType)
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

        if (!string.Equals(package.BookingType, bookingType, StringComparison.Ordinal)
            || !package.IsActive)
        {
            throw CreateInsuranceValidation(CreateUnavailablePackageMessage(bookingType));
        }

        if (insuredPassengerQuantity < 0)
        {
            throw CreateInsuranceValidation(CreateInvalidQuantityMessage(bookingType));
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
            Quantity = insuredPassengerQuantity,
            TotalAmount = package.UnitPremiumAmount * insuredPassengerQuantity,
            QuotedAt = quotedAt
        };
    }

    public static async Task<BookingInsuranceSnapshot?> ResolveQuoteInsuranceSnapshotAsync(
        IApplicationDbContext context,
        BookingInsuranceSnapshot? requestedSnapshot,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken)
    {
        if (insuredPassengerQuantity <= 0)
        {
            throw CreateInsuranceValidation("Không xác định được số hành khách để tính bảo hiểm thuê tàu.");
        }

        var package = await ResolveQuoteInsurancePackageAsync(
            context,
            requestedSnapshot?.InsurancePackageId,
            cancellationToken);
        if (package is null)
        {
            return null;
        }

        return CreateSnapshot(package, insuredPassengerQuantity, quotedAt);
    }

    public static decimal ApplyPassengerQuantityIncrease(
        Booking booking,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt)
    {
        if (booking.InsuranceSnapshot is null || insuredPassengerQuantity <= booking.InsuranceSnapshot.Quantity)
        {
            return 0m;
        }

        var previousTotal = booking.InsuranceSnapshot.TotalAmount;
        UpdateQuantity(booking.InsuranceSnapshot, insuredPassengerQuantity, quotedAt);
        var additionalAmount = booking.InsuranceSnapshot.TotalAmount - previousTotal;
        if (additionalAmount <= 0)
        {
            return 0m;
        }

        booking.SubtotalAmount += additionalAmount;
        booking.TotalAmount += additionalAmount;
        PaymentSupport.RestorePaymentSummaryFromPaidPayments(booking);
        return additionalAmount;
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
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt)
    {
        if (insuredPassengerQuantity < 0)
        {
            throw CreateInsuranceValidation(CreateInvalidQuantityMessage(snapshot.BookingType));
        }

        snapshot.Quantity = insuredPassengerQuantity;
        snapshot.TotalAmount = snapshot.UnitPremiumAmount * insuredPassengerQuantity;
        snapshot.QuotedAt = quotedAt;
        return snapshot;
    }

    private static async Task<InsurancePackage?> ResolveQuoteInsurancePackageAsync(
        IApplicationDbContext context,
        Guid? requestedInsurancePackageId,
        CancellationToken cancellationToken)
    {
        if (requestedInsurancePackageId.HasValue)
        {
            var selectedPackage = await context.Set<InsurancePackage>()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == requestedInsurancePackageId.Value, cancellationToken);
            if (selectedPackage is null)
            {
                throw CreateInsuranceValidation("Không tìm thấy gói bảo hiểm đã chọn.");
            }

            if (!IsActiveCharterInsurancePackage(selectedPackage))
            {
                throw CreateInsuranceValidation("Gói bảo hiểm đã chọn không khả dụng cho charter booking.");
            }

            return selectedPackage;
        }

        return await context.Set<InsurancePackage>()
            .AsNoTracking()
            .Where(IsActiveCharterInsurancePackageExpression())
            .OrderByDescending(x => x.IsRequired)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static BookingInsuranceSnapshot CreateSnapshot(
        InsurancePackage package,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt) =>
        new()
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
            Quantity = insuredPassengerQuantity,
            TotalAmount = package.UnitPremiumAmount * insuredPassengerQuantity,
            QuotedAt = quotedAt
        };

    private static async Task<BookingInsuranceSnapshot?> ResolveDefaultInsuranceSnapshotAsync(
        IApplicationDbContext context,
        string bookingType,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken)
    {
        if (insuredPassengerQuantity < 0)
        {
            throw CreateInsuranceValidation(CreateInvalidQuantityMessage(bookingType));
        }

        var package = await context.Set<InsurancePackage>()
            .AsNoTracking()
            .Where(x => x.BookingType == bookingType && x.IsActive)
            .OrderByDescending(x => x.IsRequired)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);

        return package is null
            ? null
            : CreateSnapshot(package, insuredPassengerQuantity, quotedAt);
    }

    private static System.Linq.Expressions.Expression<Func<InsurancePackage, bool>> IsActiveCharterInsurancePackageExpression() =>
        package => package.BookingType == Booking.CharterBookingType && package.IsActive;

    private static bool IsActiveCharterInsurancePackage(InsurancePackage package) =>
        string.Equals(package.BookingType, Booking.CharterBookingType, StringComparison.Ordinal)
        && package.IsActive;

    private static string CreateUnavailablePackageMessage(string bookingType) =>
        Booking.IsCharterBookingType(bookingType)
            ? "Gói bảo hiểm đã chọn không khả dụng cho charter booking."
            : "Gói bảo hiểm đã chọn không khả dụng cho booking thường.";

    private static string CreateInvalidQuantityMessage(string bookingType) =>
        Booking.IsCharterBookingType(bookingType)
            ? "Không xác định được số hành khách tính bảo hiểm cho booking thuê tàu."
            : "Không xác định được số hành khách tính bảo hiểm cho booking thường.";

    private static ValidationException CreateInsuranceValidation(string message) =>
        new([new ValidationFailure("insurancePackageId", message)]);
}
