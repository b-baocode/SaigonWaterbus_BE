using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CharterBookingExpirationCleanupResult(
    int ExpiredPayments,
    int ExpiredCharterBookings,
    int CleanedCharterRoutes,
    int ExpiredPassengerAddPayments = 0);

public interface ICharterBookingExpirationProcessor
{
    Task<CharterBookingExpirationCleanupResult> CleanupExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class CharterBookingExpirationProcessor : ICharterBookingExpirationProcessor
{
    private const string PassengerAddInsurancePurpose = "PassengerAddInsurance";

    private readonly IApplicationDbContext _context;
    private readonly IBoatHoldService _boatHoldService;

    public CharterBookingExpirationProcessor(
        IApplicationDbContext context,
        IBoatHoldService? boatHoldService = null)
    {
        _context = context;
        _boatHoldService = boatHoldService ?? NullBoatHoldService.Instance;
    }

    public async Task<CharterBookingExpirationCleanupResult> CleanupExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expiredPaymentCount = await ExpirePaymentLinksAsync(now, cancellationToken);
        var expiredPassengerAddPayments = await ExpirePassengerAddPaymentsAsync(now, cancellationToken);
        var expiredBookings = await ExpireCharterBookingsAsync(now, cancellationToken);
        var cleanedRoutes = await CleanupTerminalCharterRoutesAsync(cancellationToken);

        return new CharterBookingExpirationCleanupResult(
            expiredPaymentCount,
            expiredBookings,
            cleanedRoutes,
            expiredPassengerAddPayments);
    }

    private async Task<int> ExpirePaymentLinksAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var legacyPaymentCreatedCutoff = now - BookingExpirationPolicy.PaymentLinkTtl;
        var payments = await _context.Set<Payment>()
            .Include(x => x.Booking)
                .ThenInclude(x => x.Payments)
            .Where(x => x.Provider == PaymentSupport.PayOsProvider
                && x.PaymentStatus == PaymentSupport.PendingStatus
                && ((x.ExpiresAt.HasValue && x.ExpiresAt.Value <= now)
                    || (!x.ExpiresAt.HasValue && x.Created != default && x.Created <= legacyPaymentCreatedCutoff)))
            .ToListAsync(cancellationToken);

        if (payments.Count == 0)
        {
            return 0;
        }

        var changedBookings = new HashSet<Guid>();
        var expiredCount = 0;
        foreach (var payment in payments)
        {
            if (!PaymentSupport.IsPending(payment.PaymentStatus) || !PaymentSupport.IsExpired(payment, now))
            {
                continue;
            }

            payment.PaymentStatus = PaymentSupport.ExpiredStatus;
            changedBookings.Add(payment.BookingId);
            expiredCount++;
        }

        foreach (var booking in payments
                     .Select(x => x.Booking)
                     .Where(x => changedBookings.Contains(x.Id))
                     .DistinctBy(x => x.Id))
        {
            PaymentSupport.RestorePaymentSummaryFromPaidPayments(booking);
            RestorePassengerAddApprovalStateIfOnlyCheckoutExpired(booking, now);
        }

        if (expiredCount > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return expiredCount;
    }

    private static void RestorePassengerAddApprovalStateIfOnlyCheckoutExpired(Booking booking, DateTimeOffset now)
    {
        if (booking.BookingStatus != BookingStatus.PendingPayment
            || booking.RemainingAmount <= 0m
            || !booking.HoldExpiresAt.HasValue
            || booking.HoldExpiresAt.Value <= now)
        {
            return;
        }

        var hasPassengerAddPayment = booking.Payments.Any(x =>
            string.Equals(x.PaymentPurpose, PassengerAddInsurancePurpose, StringComparison.OrdinalIgnoreCase));
        var hasActivePassengerAddPayment = booking.Payments.Any(x =>
            string.Equals(x.PaymentPurpose, PassengerAddInsurancePurpose, StringComparison.OrdinalIgnoreCase)
            && PaymentSupport.IsPending(x.PaymentStatus)
            && !PaymentSupport.IsExpired(x, now));

        if (hasPassengerAddPayment && !hasActivePassengerAddPayment)
        {
            booking.BookingStatus = BookingStatus.Approved;
        }
    }

    private async Task<int> ExpireCharterBookingsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var bookings = await _context.Set<Booking>()
            .Include(x => x.CharterBoats)
            .Include(x => x.Payments)
            .Include(x => x.Promotion)
            .Include(x => x.Tickets)
            .Where(x => x.BookingType == Booking.CharterBookingType
                && (x.BookingStatus == BookingStatus.Quoted || x.BookingStatus == BookingStatus.PendingPayment)
                && x.HoldExpiresAt.HasValue
                && x.HoldExpiresAt.Value <= now)
            .ToListAsync(cancellationToken);

        if (bookings.Count == 0)
        {
            return 0;
        }

        var releaseRequests = new List<BoatHoldReleaseRequest>();
        var expiredCount = 0;
        foreach (var booking in bookings)
        {
            if (booking.Payments.Any(x => PaymentSupport.IsPayOsPayment(x) && PaymentSupport.IsPaid(x.PaymentStatus)))
            {
                continue;
            }

            foreach (var payment in booking.Payments.Where(x =>
                         PaymentSupport.IsPayOsPayment(x) && PaymentSupport.IsPending(x.PaymentStatus)))
            {
                payment.PaymentStatus = PaymentSupport.ExpiredStatus;
            }

            PaymentSupport.RestorePaymentSummaryFromPaidPayments(booking);

            var previousBoatIds = CharterBookingBoatSelectionSupport.ResolveSelectedBoatIds(booking);
            var previousDepartureDate = booking.DepartureDate;
            var previousStartTime = booking.StartTime;
            var previousRentalUnit = booking.RentalUnit;
            var previousDurationValue = booking.DurationValue.GetValueOrDefault();

            booking.BookingStatus = BookingStatus.Expired;
            booking.HoldExpiresAt = null;
            foreach (var ticket in booking.Tickets)
            {
                ticket.TicketStatus = TicketStatus.Expired;
            }

            await PointSupport.ReturnRedeemedPointsAsync(
                _context,
                booking,
                $"Hoàn điểm do charter booking {booking.BookingCode} hết hạn",
                now,
                cancellationToken);

            if (previousDepartureDate.HasValue && previousRentalUnit.HasValue && previousDurationValue > 0)
            {
                foreach (var previousBoatId in previousBoatIds)
                {
                    releaseRequests.Add(new BoatHoldReleaseRequest(
                        booking.Id,
                        previousBoatId,
                        previousDepartureDate.Value,
                        previousStartTime,
                        previousRentalUnit.Value,
                        previousDurationValue));
                }
            }

            expiredCount++;
        }

        if (expiredCount == 0)
        {
            return 0;
        }

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var request in releaseRequests)
        {
            await _boatHoldService.ReleaseAsync(
                request.BookingId,
                request.BoatId,
                request.DepartureDate,
                request.StartTime,
                request.RentalUnit,
                request.DurationValue,
                cancellationToken);
        }

        return expiredCount;
    }

    /// <summary>
    /// Charter booking đã được duyệt thêm khách mà hết hạn thanh toán BH bổ sung
    /// (12h kể từ khi admin duyệt) → auto-reject batch đã duyệt, roll-back BH, revert về Confirmed.
    /// </summary>
    private async Task<int> ExpirePassengerAddPaymentsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var bookings = await _context.Set<Booking>()
            .Include(x => x.Passengers)
            .Include(x => x.Payments)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .Where(x => x.BookingType == Booking.CharterBookingType
                && (x.BookingStatus == BookingStatus.Approved
                    || x.BookingStatus == BookingStatus.PendingPayment)
                && x.HoldExpiresAt.HasValue
                && x.HoldExpiresAt.Value <= now)
            .ToListAsync(cancellationToken);

        if (bookings.Count == 0)
        {
            return 0;
        }

        var expiredCount = 0;
        foreach (var booking in bookings)
        {
            if (!IsPassengerAddPaymentFlow(booking))
            {
                continue;
            }

            // Bỏ qua nếu booking đã được thanh toán BH top-up thành công (webhook đã chạy trước).
            if (booking.RemainingAmount <= 0)
            {
                booking.BookingStatus = BookingStatus.Confirmed;
                booking.HoldExpiresAt = null;
                expiredCount++;
                continue;
            }

            // Admin duyệt batch sẽ chuyển hành khách sang Approved trước khi tạo khoản BH bổ sung.
            // Vì mỗi booking chỉ được thêm một batch, RequestBatchId xác định chính xác batch
            // cần rollback khi hết hạn thanh toán.
            var addedPassengers = booking.Passengers
                .Where(x => x.RequestBatchId.HasValue && CharterBookingPassengerSupport.IsApproved(x))
                .ToList();

            // Passenger count trước khi thêm batch = tổng approved - batch thêm.
            // Sau khi reject, BH được tính lại theo danh sách approved còn lại.
            var previousInsuredCount = booking.Passengers
                .Count(CharterBookingPassengerSupport.IsApproved) - addedPassengers.Count;
            if (previousInsuredCount < 0)
            {
                previousInsuredCount = 0;
            }

            // Hủy pending payment BH top-up.
            foreach (var payment in booking.Payments.Where(x =>
                         string.Equals(x.PaymentPurpose, PassengerAddInsurancePurpose, StringComparison.OrdinalIgnoreCase)
                         && PaymentSupport.IsPending(x.PaymentStatus)))
            {
                payment.PaymentStatus = PaymentSupport.ExpiredStatus;
            }

            // Roll-back BH bổ sung đã apply cho batch được duyệt.
            CharterBookingInsuranceSupport.ReversePassengerQuantityIncrease(
                booking,
                previousInsuredCount);

            // Cancel vé mới đã phát hành cho batch được duyệt (chỉ cancel vé mới).
            foreach (var ticket in booking.Tickets.Where(t =>
                         t.BookingPassengerId.HasValue
                         && addedPassengers.Any(p => p.Id == t.BookingPassengerId!.Value)
                         && t.TicketStatus is TicketStatus.Active or TicketStatus.CheckedIn))
            {
                ticket.TicketStatus = TicketStatus.Cancelled;
            }

            // Đánh dấu batch đã duyệt bị reject do không thanh toán đúng hạn.
            foreach (var passenger in addedPassengers)
            {
                passenger.ApprovalStatus = CharterBookingPassengerSupport.ApprovalStatusRejected;
                passenger.ReviewedAt = now;
                passenger.ReviewNote = "Tự động từ chối do quá thời hạn thanh toán bảo hiểm bổ sung.";
            }

            // Recompute passenger counts dựa trên approved cuối cùng.
            var approvedPassengers = booking.Passengers
                .Where(CharterBookingPassengerSupport.IsApproved)
                .ToList();
            booking.PassengerCount = approvedPassengers.Count;
            booking.AdultCount = CharterBookingPassengerSupport.CountAdults(approvedPassengers);
            booking.ChildCount = CharterBookingPassengerSupport.CountChildren(approvedPassengers);

            booking.BookingStatus = BookingStatus.Confirmed;
            booking.HoldExpiresAt = null;
            expiredCount++;
        }

        if (expiredCount > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return expiredCount;
    }

    private static bool IsPassengerAddPaymentFlow(Booking booking) =>
        booking.Passengers.Any(x => x.RequestBatchId.HasValue)
        || booking.Payments.Any(x =>
            string.Equals(x.PaymentPurpose, PassengerAddInsurancePurpose, StringComparison.OrdinalIgnoreCase));

    private async Task<int> CleanupTerminalCharterRoutesAsync(CancellationToken cancellationToken)
    {
        var bookings = await _context.Set<Booking>()
            .Include(x => x.CharterRoute)
            .Where(x => x.BookingType == Booking.CharterBookingType
                && x.CharterRouteId.HasValue
                && x.CharterRoute != null
                && x.CharterRoute.Status == "Active"
                && (x.BookingStatus == BookingStatus.Cancelled || x.BookingStatus == BookingStatus.Expired))
            .ToListAsync(cancellationToken);

        if (bookings.Count == 0)
        {
            return 0;
        }

        var routeIds = bookings
            .Select(x => x.CharterRouteId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToHashSet();

        foreach (var booking in bookings)
        {
            await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(_context, booking, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var remainingActiveRouteCount = await _context.Set<Route>()
            .CountAsync(x => routeIds.Contains(x.Id) && x.Status == "Active", cancellationToken);

        return routeIds.Count - remainingActiveRouteCount;
    }

    private sealed record BoatHoldReleaseRequest(
        Guid BookingId,
        Guid BoatId,
        DateOnly DepartureDate,
        TimeOnly? StartTime,
        BoatRentalUnit RentalUnit,
        int DurationValue);
}
