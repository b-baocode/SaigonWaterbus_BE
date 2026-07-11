using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CharterBookingExpirationCleanupResult(
    int ExpiredPayments,
    int ExpiredCharterBookings);

public interface ICharterBookingExpirationProcessor
{
    Task<CharterBookingExpirationCleanupResult> CleanupExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class CharterBookingExpirationProcessor : ICharterBookingExpirationProcessor
{
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
        var expiredBookings = await ExpireCharterBookingsAsync(now, cancellationToken);

        return new CharterBookingExpirationCleanupResult(
            expiredPaymentCount,
            expiredBookings);
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
        }

        if (expiredCount > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return expiredCount;
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
            PromotionUsageSupport.DecrementUsage(booking.Promotion);

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

    private sealed record BoatHoldReleaseRequest(
        Guid BookingId,
        Guid BoatId,
        DateOnly DepartureDate,
        TimeOnly? StartTime,
        BoatRentalUnit RentalUnit,
        int DurationValue);
}
