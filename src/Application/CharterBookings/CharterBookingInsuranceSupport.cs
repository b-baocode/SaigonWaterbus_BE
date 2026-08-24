using FluentValidation.Results;
using SaigonWaterbus.Application.InsurancePackages;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingInsuranceSupport
{
    /// <summary>
    /// Resolve toàn bộ danh sách snapshot bảo hiểm cho charter booking:
    ///
    ///   - Nếu <paramref name="insuranceSelected"/> = false: không có gói nào (kể cả default).
    ///   - Nếu <paramref name="insuranceSelected"/> = true và <paramref name="insurancePackageId"/> có giá trị:
    ///       + Tự động gắn thêm gói Waterbus default (nếu có active).
    ///       + Cộng thêm gói ThirdParty mà khách chọn.
    ///   - Nếu <paramref name="insuranceSelected"/> = null (không gửi):
    ///       + Mặc định auto-attach Waterbus default (giữ hành vi cũ).
    ///       + Nếu có <paramref name="insurancePackageId"/> thì cộng thêm gói ThirdParty đó.
    /// </summary>
    /// <returns>
    /// List snapshot (rỗng = không có bảo hiểm). Phần tử đầu tiên là gói default nếu có.
    /// </returns>
    public static async Task<List<BookingInsuranceSnapshot>> ResolveRequestedInsuranceSnapshotsAsync(
        IApplicationDbContext context,
        bool? insuranceSelected,
        Guid? insurancePackageId,
        List<BookingInsuranceSnapshot>? currentSnapshots,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken,
        string bookingType = Booking.CharterBookingType)
    {
        // Case 1: customer explicitly disables insurance — clear all.
        if (insuranceSelected == false)
        {
            return new List<BookingInsuranceSnapshot>();
        }

        var requestedThirdParty = (insuranceSelected == true || insurancePackageId.HasValue)
            ? insurancePackageId
            : null;

        if (insuranceSelected == true && !insurancePackageId.HasValue)
        {
            throw CreateInsuranceValidation("Vui lòng chọn gói bảo hiểm.");
        }

        var result = new List<BookingInsuranceSnapshot>();

        // Resolve ThirdParty snapshot if requested.
        BookingInsuranceSnapshot? thirdPartySnapshot = null;
        if (requestedThirdParty.HasValue)
        {
            thirdPartySnapshot = await CreateSelectedInsuranceSnapshotAsync(
                context,
                requestedThirdParty,
                insuredPassengerQuantity,
                quotedAt,
                cancellationToken,
                bookingType);
        }

        // Resolve Waterbus default snapshot — auto-attach unless explicitly opted-out.
        var shouldAttachDefault = insuredPassengerQuantity > 0
            && (insuranceSelected != false);
        if (shouldAttachDefault)
        {
            var defaultSnapshot = await CreateWaterbusDefaultSnapshotAsync(
                context,
                insuredPassengerQuantity,
                quotedAt,
                cancellationToken,
                bookingType);

            if (defaultSnapshot is not null)
            {
                result.Add(defaultSnapshot);
            }
        }

        // Add ThirdParty last so it stacks on top of default.
        if (thirdPartySnapshot is not null)
        {
            result.Add(thirdPartySnapshot);
        }
        else if (currentSnapshots is { Count: > 0 } && insuranceSelected is null or false)
        {
            // Preserve existing snapshots if customer didn't explicitly change selection.
            foreach (var existing in currentSnapshots)
            {
                result.Add(existing);
            }
        }

        return result;
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

        if (!package.IsActive
            || !InsurancePackageSupport.IsApplicableToBookingType(package, bookingType))
        {
            throw CreateInsuranceValidation(CreateUnavailablePackageMessage(bookingType));
        }

        if (package.ProviderSource == InsuranceProviderSource.Waterbus
            && package.IsWaterbusDefault)
        {
            // Waterbus default — caller should use CreateWaterbusDefaultSnapshotAsync instead.
            // Returning it here is fine; both produce the same snapshot shape.
        }

        if (insuredPassengerQuantity < 0)
        {
            throw CreateInsuranceValidation(CreateInvalidQuantityMessage(bookingType));
        }

        return CreateSnapshot(package, insuredPassengerQuantity, quotedAt);
    }

    public static async Task<BookingInsuranceSnapshot?> CreateWaterbusDefaultSnapshotAsync(
        IApplicationDbContext context,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken,
        string bookingType = Booking.CharterBookingType)
    {
        if (insuredPassengerQuantity < 1)
        {
            return null;
        }

        var waterbusDefault = await context.Set<InsurancePackage>()
            .AsNoTracking()
            .Where(x => x.IsActive
                && (x.BookingType == InsurancePackageSupport.PassengerInsuranceBookingType
                    || x.BookingType == bookingType)
                && x.ProviderSource == InsuranceProviderSource.Waterbus
                && x.IsWaterbusDefault)
            .OrderBy(x => x.BookingType == InsurancePackageSupport.PassengerInsuranceBookingType ? 0 : 1)
            .ThenBy(x => x.Created)
            .FirstOrDefaultAsync(cancellationToken);

        return waterbusDefault is null
            ? null
            : CreateSnapshot(waterbusDefault, insuredPassengerQuantity, quotedAt);
    }

    public static async Task<List<BookingInsuranceSnapshot>> ResolveSeatBookingInsuranceSnapshotsAsync(
        IApplicationDbContext context,
        bool? insuranceSelected,
        Guid? insurancePackageId,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken,
        bool? waterbusInsuranceEnabled = null)
    {
        // Seat booking: auto-attach Waterbus default, không cho stack ThirdParty.
        // Giữ nguyên hành vi cũ.
        if (insuranceSelected == false)
        {
            return new List<BookingInsuranceSnapshot>();
        }

        if (insuranceSelected == true)
        {
            if (!insurancePackageId.HasValue)
            {
                var defaultSnapshot = await CreateWaterbusDefaultSnapshotAsync(
                    context,
                    insuredPassengerQuantity,
                    quotedAt,
                    cancellationToken,
                    Booking.SeatBookingType);
                return defaultSnapshot is null
                    ? new List<BookingInsuranceSnapshot>()
                    : new List<BookingInsuranceSnapshot> { defaultSnapshot };
            }

            var selected = await CreateSelectedInsuranceSnapshotAsync(
                context,
                insurancePackageId,
                insuredPassengerQuantity,
                quotedAt,
                cancellationToken,
                Booking.SeatBookingType);
            return selected is null
                ? new List<BookingInsuranceSnapshot>()
                : new List<BookingInsuranceSnapshot> { selected };
        }

        if (waterbusInsuranceEnabled == false)
        {
            return new List<BookingInsuranceSnapshot>();
        }

        var seatDefault = await CreateWaterbusDefaultSnapshotAsync(
            context,
            insuredPassengerQuantity,
            quotedAt,
            cancellationToken,
            Booking.SeatBookingType);
        return seatDefault is null
            ? new List<BookingInsuranceSnapshot>()
            : new List<BookingInsuranceSnapshot> { seatDefault };
    }

    public static async Task<List<BookingInsuranceSnapshot>> ResolveQuoteInsuranceSnapshotsAsync(
        IApplicationDbContext context,
        List<BookingInsuranceSnapshot>? existingSnapshots,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken)
    {
        if (insuredPassengerQuantity < 0)
        {
            throw CreateInsuranceValidation(CreateInvalidQuantityMessage(Booking.CharterBookingType));
        }

        if (insuredPassengerQuantity == 0)
        {
            return new List<BookingInsuranceSnapshot>();
        }

        var result = new List<BookingInsuranceSnapshot>();

        if (existingSnapshots is { Count: > 0 })
        {
            foreach (var snapshot in existingSnapshots)
            {
                // Re-resolve package to ensure it's still valid, then refresh quantity + total.
                var package = await context.Set<InsurancePackage>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == snapshot.InsurancePackageId, cancellationToken);

                if (package is null || !package.IsActive)
                {
                    // Skip packages no longer available.
                    continue;
                }

                result.Add(CreateSnapshot(package, insuredPassengerQuantity, quotedAt));
            }
        }

        // Auto-attach Waterbus default if no default already in result (stacking model).
        var hasDefault = result.Any(s => s.IsWaterbusDefault);
        if (!hasDefault)
        {
            var defaultSnapshot = await CreateWaterbusDefaultSnapshotAsync(
                context,
                insuredPassengerQuantity,
                quotedAt,
                cancellationToken,
                Booking.CharterBookingType);
            if (defaultSnapshot is not null)
            {
                // Insert default first so it sits at index 0, before any ThirdParty snapshots.
                result.Insert(0, defaultSnapshot);
            }
        }

        return result;
    }

    public static decimal ApplyPassengerQuantityIncrease(
        Booking booking,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt)
    {
        if ((booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>()).Count == 0 || insuredPassengerQuantity <= 0)
        {
            return 0m;
        }

        var previousTotal = booking.GetTotalInsuranceAmount();
        foreach (var snapshot in (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>()).ToList())
        {
            UpdateQuantity(snapshot, insuredPassengerQuantity, quotedAt);
        }
        var additionalAmount = booking.GetTotalInsuranceAmount() - previousTotal;
        if (additionalAmount <= 0)
        {
            return 0m;
        }

        booking.SubtotalAmount += additionalAmount;
        booking.TotalAmount += additionalAmount;
        PaymentSupport.RestorePaymentSummaryFromPaidPayments(booking);
        return additionalAmount;
    }

    public static IReadOnlyList<CharterBookingInsuranceDto> ToDtos(IEnumerable<BookingInsuranceSnapshot> snapshots) =>
        snapshots.Select(ToDto).Where(dto => dto is not null).Select(dto => dto!).ToList();

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
                snapshot.ImageUrl,
                snapshot.UnitPremiumAmount,
                snapshot.CoverageAmount,
                snapshot.Currency,
                snapshot.Quantity,
                snapshot.TotalAmount,
                snapshot.Conditions,
                snapshot.TermsUrl,
                snapshot.QuotedAt,
                Selected: true,
                ProviderSource: snapshot.ProviderSource);

    private static void UpdateQuantity(
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
            ImageUrl = package.ImageUrl,
            UnitPremiumAmount = package.UnitPremiumAmount,
            CoverageAmount = package.CoverageAmount,
            Currency = package.Currency,
            Conditions = package.Conditions,
            TermsUrl = package.TermsUrl,
            Quantity = insuredPassengerQuantity,
            TotalAmount = package.UnitPremiumAmount * insuredPassengerQuantity,
            QuotedAt = quotedAt,
            IsWaterbusDefault = package.IsWaterbusDefault,
            ProviderSource = package.ProviderSource
        };

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