using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

public sealed record TripStatusAutoSyncResult(
    int DepartedTripCount,
    int ArrivedTripCount,
    int CompletedBookingCount);

public interface ITripStatusAutoSyncProcessor
{
    Task<TripStatusAutoSyncResult> SyncAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

internal sealed class TripStatusAutoSyncProcessor(IApplicationDbContext context)
    : ITripStatusAutoSyncProcessor
{
    public async Task<TripStatusAutoSyncResult> SyncAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var arrivedTrips = await context.Set<Trip>()
            .Where(x => x.TripStatus != TripStatus.Cancelled
                && x.TripStatus != TripStatus.Completed
                && x.ArrivalTime <= now)
            .ToListAsync(cancellationToken);

        var completedBookingCount = 0;
        foreach (var trip in arrivedTrips)
        {
            trip.TripStatus = TripStatus.Completed;
            trip.LastStatusChangedAt = now;
        }

        var staleCompletedTrips = await context.Set<Trip>()
            .Where(x => x.TripStatus == TripStatus.Completed
                && x.SourceBookingId.HasValue
                && context.Set<Booking>().Any(booking =>
                    booking.Id == x.SourceBookingId.Value
                    && booking.BookingStatus == BookingStatus.Confirmed))
            .ToListAsync(cancellationToken);

        var completionCandidates = arrivedTrips
            .Concat(staleCompletedTrips)
            .DistinctBy(x => x.Id);
        foreach (var trip in completionCandidates)
        {
            if (await CharterBookingTripSupport.CompleteLinkedBookingAsync(
                    context,
                    trip,
                    now,
                    cancellationToken))
            {
                completedBookingCount++;
            }
        }

        var departedTrips = await context.Set<Trip>()
            .Where(x => x.TripStatus != TripStatus.Cancelled
                && x.TripStatus != TripStatus.Completed
                && x.TripStatus != TripStatus.InProgress
                && x.DepartureTime <= now
                && x.ArrivalTime > now)
            .ToListAsync(cancellationToken);

        foreach (var trip in departedTrips)
        {
            trip.TripStatus = TripStatus.InProgress;
            trip.LastStatusChangedAt = now;
        }

        if (arrivedTrips.Count > 0 || departedTrips.Count > 0 || completedBookingCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return new TripStatusAutoSyncResult(
            departedTrips.Count,
            arrivedTrips.Count,
            completedBookingCount);
    }
}
