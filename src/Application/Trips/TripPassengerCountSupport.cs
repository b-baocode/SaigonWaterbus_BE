using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

public static class TripPassengerCountSupport
{
    public static async Task<Dictionary<Guid, int>> LoadOnboardPassengerCountsByTripIdAsync(
        IApplicationDbContext context,
        IEnumerable<Guid> tripIds,
        CancellationToken cancellationToken)
    {
        var tripIdList = tripIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();
        if (tripIdList.Length == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var onboardTickets = await context.Set<Ticket>()
            .AsNoTracking()
            .Include(x => x.Booking)
            .Include(x => x.BookingPassenger)
                .ThenInclude(x => x!.TripSeat)
                    .ThenInclude(x => x!.Seat)
            .Where(x => x.TicketStatus == TicketStatus.CheckedIn
                && !x.CheckedOutAt.HasValue
                && x.Booking.BookingStatus == BookingStatus.Confirmed
                && x.BookingPassengerId.HasValue
                && ((x.BookingPassenger!.TripId.HasValue
                        && tripIdList.Contains(x.BookingPassenger.TripId.Value))
                    || (!x.BookingPassenger.TripId.HasValue
                        && x.Booking.TripId.HasValue
                        && tripIdList.Contains(x.Booking.TripId.Value))))
            .ToListAsync(cancellationToken);

        if (onboardTickets.Count == 0)
        {
            return tripIdList.ToDictionary(x => x, _ => 0);
        }

        var bookingIds = onboardTickets
            .Select(x => x.BookingId)
            .Distinct()
            .ToArray();
        var passengersByBookingId = (await context.Set<BookingPassenger>()
                .AsNoTracking()
                .Include(x => x.TripSeat)
                    .ThenInclude(x => x!.Seat)
                .Where(x => bookingIds.Contains(x.BookingId)
                    && ((x.TripId.HasValue && tripIdList.Contains(x.TripId.Value))
                        || (!x.TripId.HasValue
                            && x.Booking.TripId.HasValue
                            && tripIdList.Contains(x.Booking.TripId.Value))))
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.BookingId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var passengerIdsByTripId = tripIdList.ToDictionary(
            x => x,
            _ => new HashSet<Guid>());

        foreach (var ticket in onboardTickets)
        {
            var tripId = ResolveTicketTripId(ticket);
            if (!tripId.HasValue || !passengerIdsByTripId.ContainsKey(tripId.Value))
            {
                continue;
            }

            if (!passengersByBookingId.TryGetValue(ticket.BookingId, out var bookingPassengers))
            {
                bookingPassengers = ticket.BookingPassenger is null
                    ? []
                    : [ticket.BookingPassenger];
            }

            var legPassengers = bookingPassengers
                .Where(x => ResolvePassengerTripId(x, ticket.Booking) == tripId.Value)
                .ToList();
            var representedPassengers = LapInfantTicketSupport.ResolvePassengersRepresentedByTicket(
                legPassengers,
                ticket.BookingPassenger);

            foreach (var passenger in representedPassengers)
            {
                passengerIdsByTripId[tripId.Value].Add(passenger.Id);
            }
        }

        return passengerIdsByTripId.ToDictionary(x => x.Key, x => x.Value.Count);
    }

    public static async Task<int> LoadOnboardPassengerCountAsync(
        IApplicationDbContext context,
        Guid? tripId,
        CancellationToken cancellationToken)
    {
        if (!tripId.HasValue)
        {
            return 0;
        }

        var counts = await LoadOnboardPassengerCountsByTripIdAsync(
            context,
            [tripId.Value],
            cancellationToken);
        return counts.GetValueOrDefault(tripId.Value);
    }

    private static Guid? ResolveTicketTripId(Ticket ticket) =>
        ticket.BookingPassenger?.TripId ?? ticket.Booking.TripId;

    private static Guid? ResolvePassengerTripId(BookingPassenger passenger, Booking booking) =>
        passenger.TripId ?? booking.TripId;
}
