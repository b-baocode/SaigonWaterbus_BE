using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

/// <summary>
/// Sightseeing/tour chỉ nên chạy khi có khách đã xác nhận. Trước giờ khởi hành 5 phút,
/// nếu chưa có booking Confirmed/Completed nào thì tự hủy chuyến để tàu không chạy rỗng.
/// </summary>
public static class SightseeingTripAutoCancellationSupport
{
    public static readonly TimeSpan EmptyTripCancellationLeadTime = TimeSpan.FromMinutes(5);
    private const string EmptyTripCancellationNote =
        "Tự động hủy chuyến sightseeing vì không có khách trước giờ khởi hành 5 phút.";

    public static async Task<int> CancelDueEmptySightseeingTripsAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var dueAt = now.Add(EmptyTripCancellationLeadTime);
        var candidates = await context.Set<Trip>()
            .Include(x => x.Route)
            .Where(x => x.Route.RouteType == RouteTypes.SightseeingLoop
                     && x.TripType == TripTypes.Regular
                     && (x.TripStatus == TripStatus.Scheduled
                         || x.TripStatus == TripStatus.Boarding
                         || x.TripStatus == TripStatus.Delayed)
                     && (x.AdjustedDepartureTime ?? x.DepartureTime) <= dueAt)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var candidateIds = candidates.Select(x => x.Id).ToList();
        var tripsWithCustomers = await context.Set<BookingPassenger>()
            .Where(x => x.TripId.HasValue && candidateIds.Contains(x.TripId.Value))
            .Where(x => x.Booking.BookingType == Booking.SeatBookingType
                     && (x.Booking.BookingStatus == BookingStatus.Confirmed
                         || x.Booking.BookingStatus == BookingStatus.Completed))
            .Select(x => x.TripId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var tripsWithCustomersSet = tripsWithCustomers.ToHashSet();

        var tripsToCancel = candidates
            .Where(x => !tripsWithCustomersSet.Contains(x.Id))
            .ToList();
        if (tripsToCancel.Count == 0)
        {
            return 0;
        }

        foreach (var trip in tripsToCancel)
        {
            trip.TripStatus = TripStatus.Cancelled;
            trip.StatusNote = EmptyTripCancellationNote;
        }

        await ExpirePendingBookingsForCancelledTripsAsync(
            context,
            tripsToCancel.Select(x => x.Id).ToList(),
            now,
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return tripsToCancel.Count;
    }

    private static async Task ExpirePendingBookingsForCancelledTripsAsync(
        IApplicationDbContext context,
        IReadOnlyList<Guid> tripIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pendingBookings = await context.Set<Booking>()
            .Include(x => x.Passengers)
            .Include(x => x.Payments)
            .Where(x => x.BookingType == Booking.SeatBookingType
                     && x.BookingStatus == BookingStatus.PendingPayment
                     && x.Passengers.Any(p => p.TripId.HasValue && tripIds.Contains(p.TripId.Value)))
            .ToListAsync(cancellationToken);

        foreach (var booking in pendingBookings)
        {
            booking.BookingStatus = BookingStatus.Expired;
            foreach (var payment in booking.Payments
                         .Where(x => PaymentSupport.IsPayOsPayment(x)
                                  && PaymentSupport.IsPending(x.PaymentStatus)))
            {
                payment.PaymentStatus = PaymentSupport.ExpiredStatus;
            }

            await PointSupport.ReturnRedeemedPointsAsync(
                context,
                booking,
                $"Hoàn điểm do booking {booking.BookingCode} hết hạn vì chuyến sightseeing bị hủy",
                now,
                cancellationToken);
        }
    }
}
