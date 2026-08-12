using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Payments;

public static class PaymentPendingExpirationSupport
{
    public static readonly TimeSpan PendingExpirationDuration = TimeSpan.FromMinutes(8);

    public static async Task<int> CancelOverduePendingPaymentsAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var cutoff = now.Subtract(PendingExpirationDuration);

        var overduePayments = await context.Set<Payment>()
            .Include(x => x.Booking)
                .ThenInclude(x => x.Tickets)
            .Include(x => x.Booking)
                .ThenInclude(x => x.Passengers)
            .Include(x => x.Booking)
                .ThenInclude(x => x.CharterBoats)
            .Include(x => x.Booking)
                .ThenInclude(x => x.Payments)
            .Include(x => x.Booking)
                .ThenInclude(x => x.ItineraryStops)
            .Where(x => x.Provider == PaymentSupport.PayOsProvider
                     && x.PaymentStatus == PaymentSupport.PendingStatus
                     && x.Created <= cutoff)
            .ToListAsync(cancellationToken);

        if (overduePayments.Count == 0)
        {
            return 0;
        }

        var cancelledCount = 0;

        foreach (var payment in overduePayments)
        {
            payment.PaymentStatus = PaymentSupport.CancelledStatus;
            var booking = payment.Booking;

            if (booking.BookingStatus == BookingStatus.Cancelled)
            {
                continue;
            }

            booking.BookingStatus = BookingStatus.Cancelled;
            booking.HoldExpiresAt = null;

            foreach (var ticket in booking.Tickets)
            {
                ticket.TicketStatus = TicketStatus.Cancelled;
            }

            if (Booking.IsCharterBookingType(booking.BookingType))
            {
                await CharterBookingTripSupport.CancelLinkedTripsAsync(
                    context,
                    booking.Id,
                    $"Charter booking {booking.BookingCode} đã bị hủy do payment PayOS hết hạn.",
                    cancellationToken);

                await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(
                    context,
                    booking,
                    cancellationToken);
            }
            else
            {
                foreach (var passenger in booking.Passengers)
                {
                    passenger.TripId = null;
                    passenger.TripSeatId = null;
                }
            }

            cancelledCount++;
        }

        if (cancelledCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return cancelledCount;
    }
}
