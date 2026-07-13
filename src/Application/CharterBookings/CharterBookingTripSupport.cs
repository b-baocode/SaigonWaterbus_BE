using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingTripSupport
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Chuoi ben cua booking theo thu tu: ben di -> cac diem dung -> ben den.</summary>
    public static IReadOnlyList<Guid> BuildStationSequence(Booking booking)
    {
        var sequence = new List<Guid>();
        if (booking.FromStationId.HasValue)
        {
            sequence.Add(booking.FromStationId.Value);
        }

        sequence.AddRange(booking.ItineraryStops
            .OrderBy(x => x.StopOrder)
            .Select(x => x.StationId));

        if (booking.ToStationId.HasValue)
        {
            sequence.Add(booking.ToStationId.Value);
        }

        return sequence;
    }

    /// <summary>
    /// Route chi duoc coi la khop khi chuoi RouteStops (theo StopOrder) trung het voi chuoi ben
    /// cua booking — dung ben, dung thu tu, dung so luong. Nhieu route cung khop thi uu tien
    /// CharterReference, sau do route tao gan nhat.
    /// </summary>
    public static async Task<Route?> FindMatchingRouteAsync(
        IApplicationDbContext context,
        IReadOnlyList<Guid> stationSequence,
        CancellationToken cancellationToken)
    {
        if (stationSequence.Count < 2)
        {
            return null;
        }

        var firstStationId = stationSequence[0];
        var candidates = await context.Set<Route>()
            .AsNoTracking()
            .Include(x => x.RouteStops)
            .Where(x => x.Status == "Active"
                && x.RouteStops.Count == stationSequence.Count
                && x.RouteStops.Any(stop => stop.StationId == firstStationId))
            .ToListAsync(cancellationToken);

        return candidates
            .Where(route => route.RouteStops
                .OrderBy(stop => stop.StopOrder)
                .Select(stop => stop.StationId)
                .SequenceEqual(stationSequence))
            .OrderByDescending(route =>
                string.Equals(route.RouteType, RouteTypes.CharterReference, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(route => route.Created)
            .FirstOrDefault();
    }

    public static DateTimeOffset ResolveDepartureTimeUtc(Booking booking)
    {
        var startTime = booking.StartTime ?? new TimeOnly(0, 0);
        return new DateTimeOffset(booking.DepartureDate!.Value.ToDateTime(startTime), VietnamOffset)
            .ToUniversalTime();
    }

    public static DateTimeOffset ResolveArrivalTimeUtc(DateTimeOffset departureTimeUtc, Booking booking)
    {
        var rentalUnit = CharterBookingRoutePricingSupport.ResolveRentalUnit(booking);
        var durationValue = CharterBookingRoutePricingSupport.ResolveRequestedDurationValue(booking);

        return rentalUnit == BoatRentalUnit.Day
            ? departureTimeUtc.AddDays(durationValue)
            : departureTimeUtc.AddHours(durationValue);
    }

    public static string BuildTripCode(Booking booking, int boatOrder) =>
        $"CH-{booking.DepartureDate!.Value:yyyyMMdd}-{booking.BookingCode}-{boatOrder}";

    /// <summary>Huy cac trip da sinh tu charter booking (khi booking bi huy/hoan tien).</summary>
    public static async Task<int> CancelLinkedTripsAsync(
        IApplicationDbContext context,
        Guid bookingId,
        string statusNote,
        CancellationToken cancellationToken)
    {
        var trips = await context.Set<Trip>()
            .Where(x => x.SourceBookingId == bookingId
                && x.TripStatus != TripStatus.Cancelled
                && x.TripStatus != TripStatus.Completed)
            .ToListAsync(cancellationToken);

        foreach (var trip in trips)
        {
            trip.TripStatus = TripStatus.Cancelled;
            trip.StatusNote = statusNote;
        }

        return trips.Count;
    }
}
