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
        int insuredSeatQuantity,
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
                insuredSeatQuantity,
                quotedAt,
                cancellationToken);
        }

        if (insurancePackageId.HasValue)
        {
            return await CreateSelectedInsuranceSnapshotAsync(
                context,
                insurancePackageId,
                insuredSeatQuantity,
                quotedAt,
                cancellationToken);
        }

        return currentSnapshot is null
            ? null
            : UpdateQuantity(currentSnapshot, insuredSeatQuantity, quotedAt);
    }

    public static async Task<BookingInsuranceSnapshot?> CreateSelectedInsuranceSnapshotAsync(
        IApplicationDbContext context,
        Guid? insurancePackageId,
        int insuredSeatQuantity,
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

        if (insuredSeatQuantity < 0)
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
            Quantity = insuredSeatQuantity,
            TotalAmount = package.UnitPremiumAmount * insuredSeatQuantity,
            QuotedAt = quotedAt
        };
    }

    public static async Task<BookingInsuranceSnapshot?> ResolveQuoteInsuranceSnapshotAsync(
        IApplicationDbContext context,
        BookingInsuranceSnapshot? requestedSnapshot,
        IReadOnlyList<QuoteBoatSelection> selectedBoats,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken)
    {
        var insuredSeatQuantity = selectedBoats.Sum(x => x.Boat.SeatCount);
        if (insuredSeatQuantity <= 0)
        {
            throw CreateInsuranceValidation("Không xác định được tổng số ghế của tàu để tính bảo hiểm thuê tàu.");
        }

        var package = await ResolveQuoteInsurancePackageAsync(
            context,
            requestedSnapshot?.InsurancePackageId,
            cancellationToken);
        if (package is null)
        {
            return null;
        }

        return CreateSnapshot(package, insuredSeatQuantity, quotedAt);
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
        int insuredSeatQuantity,
        DateTimeOffset quotedAt)
    {
        if (insuredSeatQuantity < 0)
        {
            throw CreateInsuranceValidation("Không xác định được số ghế tính bảo hiểm cho booking thuê tàu.");
        }

        snapshot.Quantity = insuredSeatQuantity;
        snapshot.TotalAmount = snapshot.UnitPremiumAmount * insuredSeatQuantity;
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
        int insuredSeatQuantity,
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
            Quantity = insuredSeatQuantity,
            TotalAmount = package.UnitPremiumAmount * insuredSeatQuantity,
            QuotedAt = quotedAt
        };

    private static System.Linq.Expressions.Expression<Func<InsurancePackage, bool>> IsActiveCharterInsurancePackageExpression() =>
        package => package.BookingType == Booking.CharterBookingType && package.IsActive;

    private static bool IsActiveCharterInsurancePackage(InsurancePackage package) =>
        string.Equals(package.BookingType, Booking.CharterBookingType, StringComparison.Ordinal)
        && package.IsActive;

    private static ValidationException CreateInsuranceValidation(string message) =>
        new([new ValidationFailure("insurancePackageId", message)]);
}
