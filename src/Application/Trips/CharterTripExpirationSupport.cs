using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

public static class CharterTripExpirationSupport
{
    public static readonly TimeSpan ExpirationGracePeriod = TimeSpan.FromHours(2);
    public static readonly TimeSpan DeleteGracePeriod = TimeSpan.FromHours(24);

    public static async Task<(int Completed, int Deleted)> CompleteAndDeleteOverdueCharterTripsAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var completeCutoff = now.Subtract(ExpirationGracePeriod);
        var terminalDeleteCutoff = now.Subtract(DeleteGracePeriod);
        var legacyDeleteCutoff = now.Subtract(ExpirationGracePeriod + DeleteGracePeriod);

        var overdueTrips = await context.Set<Trip>()
            .Include(x => x.Route)
            .Include(x => x.Boat)
            .Include(x => x.TripStops)
            .Where(x => x.TripType == TripTypes.Charter
                     && x.TripStatus != TripStatus.Cancelled
                     && x.TripStatus != TripStatus.Completed
                     && (x.AdjustedDepartureTime ?? x.DepartureTime) <= completeCutoff)
            .ToListAsync(cancellationToken);

        var completedCount = 0;
        var deletedCount = 0;
        var notifications = new List<Notification>();

        foreach (var trip in overdueTrips)
        {
            trip.TripStatus = TripStatus.Completed;
            trip.StatusNote = $"Tự động hoàn tất do quá ngày khởi hành ({now:dd/MM/yyyy HH:mm})";
            trip.LastStatusChangedAt = now;

            await ExpireTicketsForTripAsync(context, trip, cancellationToken);

            var bookingNotifications = await NotificationSupport.AddTripCompletedReviewInviteNotificationsAsync(
                context,
                trip,
                TripStatus.Scheduled,
                now,
                cancellationToken);
            notifications.AddRange(bookingNotifications);

            completedCount++;
        }

        if (completedCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        var tripsToDelete = await context.Set<Trip>()
            .Include(x => x.TripStops)
            .Where(x => x.TripType == TripTypes.Charter
                && (x.TripStatus == TripStatus.Completed || x.TripStatus == TripStatus.Cancelled)
                && ((x.LastStatusChangedAt.HasValue && x.LastStatusChangedAt.Value <= terminalDeleteCutoff)
                    || (!x.LastStatusChangedAt.HasValue
                        && (x.AdjustedDepartureTime ?? x.DepartureTime) <= legacyDeleteCutoff)))
            .ToListAsync(cancellationToken);

        if (tripsToDelete.Count == 0)
        {
            return (completedCount, 0);
        }

        foreach (var trip in tripsToDelete)
        {
            trip.TripStops.Clear();
            context.Set<Trip>().Remove(trip);
            deletedCount++;
        }

        if (deletedCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return (completedCount, deletedCount);
    }

    private static async Task ExpireTicketsForTripAsync(
        IApplicationDbContext context,
        Trip trip,
        CancellationToken cancellationToken)
    {
        var tickets = await context.Set<Booking>()
            .Where(b => b.TripId == trip.Id)
            .SelectMany(b => b.Tickets)
            .ToListAsync(cancellationToken);

        foreach (var ticket in tickets)
        {
            switch (ticket.TicketStatus)
            {
                case TicketStatus.Active:
                case TicketStatus.CheckedIn:
                    ticket.TicketStatus = TicketStatus.Expired;
                    break;
                case TicketStatus.CheckedOut:
                    break;
            }
        }
    }
}
