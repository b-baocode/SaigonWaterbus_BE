using FluentValidation.Results;
using SaigonWaterbus.Application.InsurancePackages;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingInsuranceSupport
{
    /// <summary>
    /// Resolve toàn bộ danh sách snapshot bảo hiểm cho charter booking.
    ///
    ///   Charter BẮT BUỘC phải chọn 1 gói bảo hiểm (FE chịu trách nhiệm lookup và gửi
    ///   <paramref name="insurancePackageId"/>). FE có thể chọn gói Waterbus default HOẶC
    ///   gói ThirdParty — chỉ lưu 1 snapshot duy nhất, không stacking.
    ///
    ///   - <paramref name="insurancePackageId"/> null trong khi có hành khách → throw validation.
    ///   - <paramref name="insuranceSelected"/> được bỏ qua với charter vì bắt buộc phải có bảo hiểm.
    ///     Trường này chỉ còn ý nghĩa cho seat booking.
    /// </summary>
    /// <returns>
    /// List snapshot gồm đúng 1 phần tử (gói đã chọn). Rỗng khi không có hành khách.
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
        // Charter: nếu không có hành khách thì không phát sinh bảo hiểm.
        if (insuredPassengerQuantity < 1)
        {
            return new List<BookingInsuranceSnapshot>();
        }

        // Charter BẮT BUỘC phải chọn 1 gói — FE lookup và gửi ID.
        if (!insurancePackageId.HasValue)
        {
            throw CreateInsuranceValidation("Vui lòng chọn gói bảo hiểm cho charter.");
        }

        var selected = await CreateSelectedInsuranceSnapshotAsync(
            context,
            insurancePackageId,
            insuredPassengerQuantity,
            quotedAt,
            cancellationToken,
            bookingType);

        if (selected is null)
        {
            // Không tìm thấy gói hoặc gói không còn khả dụng → đã throw bên trong helper.
            return new List<BookingInsuranceSnapshot>();
        }

        return new List<BookingInsuranceSnapshot> { selected };
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
        bool? waterbusInsuranceEnabled = null,
        List<BookingInsuranceSnapshot>? currentSnapshots = null)
    {
        // STACKING model cho seat booking:
        // 1. Luôn auto-attach Waterbus default ĐẦU TIÊN (đồng bộ EffectivePrice ở seat map).
        // 2. Nếu insuranceSelected == true + packageId khác Waterbus default → thêm gói đó LÊN TRÊN.
        // 3. Nếu insuranceSelected == false + waterbusInsuranceEnabled == false → KHÔNG attach gì.
        //    (Chỉ opt-out khi client chủ động tắt cả 2 cờ.)
        // 4. Nếu insuranceSelected == null: luôn auto-attach Waterbus default.

        // Case 3: opt-out hoàn toàn.
        if (insuranceSelected == false && waterbusInsuranceEnabled == false)
        {
            return new List<BookingInsuranceSnapshot>();
        }

        // Case 4: insuranceSelected == null → luôn auto-attach Waterbus default.
        if (insuranceSelected != true)
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

        // Case 1 & 2: insuranceSelected == true.
        var result = new List<BookingInsuranceSnapshot>();

        // Auto-attach Waterbus default ĐẦU TIÊN (base insurance).
        var defaultInsurance = await CreateWaterbusDefaultSnapshotAsync(
            context,
            insuredPassengerQuantity,
            quotedAt,
            cancellationToken,
            Booking.SeatBookingType);
        if (defaultInsurance is not null)
        {
            result.Add(defaultInsurance);
        }

        // Nếu client chọn 1 gói cụ thể (ThirdParty hoặc default khác):
        if (insurancePackageId.HasValue)
        {
            var selected = await CreateSelectedInsuranceSnapshotAsync(
                context,
                insurancePackageId,
                insuredPassengerQuantity,
                quotedAt,
                cancellationToken,
                Booking.SeatBookingType);

            if (selected is not null)
            {
                // Chỉ thêm nếu KHÁC Waterbus default (tránh trùng lặp).
                if (!selected.IsWaterbusDefault)
                {
                    result.Add(selected);
                }
            }
        }
        // else: không truyền packageId → chỉ có Waterbus default.

        return result;
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

        // Charter quote (Replace model): giữ nguyên snapshot đã chọn từ trước,
        // refresh lại quantity + totalAmount theo số hành khách hiện tại.
        // KHÔNG tự động attach Waterbus default — khách đã chọn 1 gói duy nhất.
        if (existingSnapshots is not { Count: > 0 })
        {
            return new List<BookingInsuranceSnapshot>();
        }

        var result = new List<BookingInsuranceSnapshot>();
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

    public static async Task<decimal> ApplyPassengerQuantityIncreaseAsync(
        IApplicationDbContext context,
        Booking booking,
        int insuredPassengerQuantity,
        DateTimeOffset quotedAt,
        CancellationToken cancellationToken)
    {
        var wasFullyPaidByPoints = booking.PointsUsed > 0
            && booking.TotalAmount <= 0m
            && booking.RemainingAmount <= 0m
            && booking.Payments.Any(x =>
                PaymentSupport.IsSettlementPayment(x)
                && PaymentSupport.IsPaid(x.PaymentStatus)
                && string.Equals(
                    x.PaymentMethod,
                    PaymentSupport.PointsPaymentMethod,
                    StringComparison.OrdinalIgnoreCase));

        var additionalAmount = ApplyPassengerQuantityIncrease(
            booking,
            insuredPassengerQuantity,
            quotedAt);
        if (!wasFullyPaidByPoints
            || additionalAmount <= 0m
            || !booking.UserId.HasValue
            || additionalAmount > int.MaxValue)
        {
            return additionalAmount;
        }

        var user = await context.Set<User>()
            .SingleOrDefaultAsync(x => x.Id == booking.UserId.Value, cancellationToken);
        if (user is null || user.PointBalance <= 0)
        {
            return additionalAmount;
        }

        var pointsRequired = (int)decimal.Ceiling(additionalAmount);
        var pointsToUse = Math.Min(user.PointBalance, pointsRequired);

        PointSupport.AddTransaction(
            context,
            user,
            booking.Id,
            PointTransactionTypes.Redeem,
            -pointsToUse,
            $"Dùng điểm thanh toán bảo hiểm bổ sung booking {booking.BookingCode}",
            quotedAt);
        booking.PointsUsed += pointsToUse;
        booking.TotalAmount = Math.Max(0m, booking.TotalAmount - pointsToUse);
        PaymentSupport.RestorePaymentSummaryFromPaidPayments(booking);
        booking.PaymentStatus = booking.RemainingAmount <= 0m
            ? BookingPaymentStatusExtensions.PaidValue
            : BookingPaymentStatusExtensions.DepositPaidValue;
        return additionalAmount;
    }

    public static async Task<decimal> ReversePassengerQuantityIncreaseAsync(
        IApplicationDbContext context,
        Booking booking,
        int previousInsuredPassengerQuantity,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reducedAmount = ReversePassengerQuantityIncrease(
            booking,
            previousInsuredPassengerQuantity);
        if (reducedAmount <= 0m || booking.PointsUsed <= 0 || !booking.UserId.HasValue)
        {
            return reducedAmount;
        }

        var payableBeforePoints = Math.Max(0m, booking.SubtotalAmount - booking.DiscountAmount);
        var maximumApplicablePoints = (int)Math.Min(
            int.MaxValue,
            decimal.Floor(payableBeforePoints));
        var pointsToReturn = Math.Max(0, booking.PointsUsed - maximumApplicablePoints);
        if (pointsToReturn <= 0)
        {
            return reducedAmount;
        }

        var user = await context.Set<User>()
            .SingleOrDefaultAsync(x => x.Id == booking.UserId.Value, cancellationToken);
        if (user is null)
        {
            return reducedAmount;
        }

        PointSupport.AddTransaction(
            context,
            user,
            booking.Id,
            PointTransactionTypes.RedeemCancelled,
            pointsToReturn,
            $"Hoàn điểm bảo hiểm bổ sung hết hạn booking {booking.BookingCode}",
            now);
        booking.PointsUsed -= pointsToReturn;
        PaymentSupport.RestorePaymentSummaryFromPaidPayments(booking);
        booking.PaymentStatus = booking.RemainingAmount <= 0m
            ? BookingPaymentStatusExtensions.PaidValue
            : BookingPaymentStatusExtensions.DepositPaidValue;
        return reducedAmount;
    }

    /// <summary>
    /// Roll back phần bảo hiểm bổ sung đã apply cho batch hành khách mới bị reject (do hết hạn thanh toán BH hoặc admin từ chối).
    /// Trả về số tiền BH đã được trừ ra khỏi Subtotal/TotalAmount để caller dùng cho audit.
    /// </summary>
    public static decimal ReversePassengerQuantityIncrease(
        Booking booking,
        int previousInsuredPassengerQuantity)
    {
        if ((booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>()).Count == 0)
        {
            return 0m;
        }

        var previousTotal = booking.GetTotalInsuranceAmount();
        var now = DateTimeOffset.UtcNow;
        foreach (var snapshot in (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>()).ToList())
        {
            UpdateQuantity(snapshot, previousInsuredPassengerQuantity, now);
        }
        var reducedAmount = previousTotal - booking.GetTotalInsuranceAmount();
        if (reducedAmount <= 0)
        {
            return 0m;
        }

        booking.SubtotalAmount = Math.Max(0m, booking.SubtotalAmount - reducedAmount);
        booking.TotalAmount = Math.Max(0m, booking.TotalAmount - reducedAmount);
        PaymentSupport.RestorePaymentSummaryFromPaidPayments(booking);
        return reducedAmount;
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
