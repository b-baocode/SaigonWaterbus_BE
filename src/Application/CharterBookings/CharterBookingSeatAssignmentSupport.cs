using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

/// <summary>
/// Reserves physical seats as soon as a charter has been paid in full. The reservation is
/// independent from an operational trip, so tickets and PDFs can show a seat immediately.
/// Once Operations creates the trips, the same physical seats are mapped to TripSeat rows.
/// </summary>
internal static class CharterBookingSeatAssignmentSupport
{
    private const string PaidBookingPaymentStatus = BookingPaymentStatusExtensions.PaidValue;

    public static async Task AssignApprovedPassengersAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var charterBoats = await context.CharterBookingBoats
            .Where(x => x.BookingId == booking.Id)
            .OrderBy(x => x.BoatOrder)
            .ToListAsync(cancellationToken);
        if (charterBoats.Count == 0)
        {
            return;
        }

        var unavailableSeatIds = new HashSet<Guid>();
        if (charterBoats.All(x => x.TripId.HasValue))
        {
            var tripIds = charterBoats.Select(x => x.TripId!.Value).ToArray();
            var unavailableSeats = await context.Set<TripSeat>()
                .Where(x => tripIds.Contains(x.TripId)
                    && (x.Status == TripSeat.StatusBlocked
                        || context.Set<BookingPassenger>().Any(p =>
                            p.BookingId != booking.Id && p.TripSeatId == x.Id)))
                .Select(x => x.SeatId)
                .ToListAsync(cancellationToken);
            unavailableSeatIds.UnionWith(unavailableSeats);
        }

        var boatIds = charterBoats.Select(x => x.BoatId).ToArray();
        var activeSeats = await context.Seats
            .Where(x => boatIds.Contains(x.BoatId)
                && x.IsActive
                && !unavailableSeatIds.Contains(x.Id))
            .OrderBy(x => x.Deck)
            .ThenBy(x => x.Row)
            .ThenBy(x => x.Column)
            .ToListAsync(cancellationToken);
        var seatsByBoatId = activeSeats
            .GroupBy(x => x.BoatId)
            .ToDictionary(x => x.Key, x => x.ToList());
        var activeSeatIds = activeSeats.Select(x => x.Id).ToHashSet();

        var passengers = booking.Passengers
            .Where(CharterBookingPassengerSupport.IsApproved)
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .ToList();
        if (passengers.Count == 0)
        {
            return;
        }

        // Preserve old assignments when this runs against a charter whose trip already exists.
        foreach (var passenger in passengers.Where(x => !x.CharterSeatId.HasValue && x.TripSeat is not null))
        {
            passenger.CharterSeatId = passenger.TripSeat!.SeatId;
            passenger.CharterSeat = passenger.TripSeat.Seat;
        }

        var assignedSeatIds = new HashSet<Guid>();
        foreach (var passenger in passengers)
        {
            if (passenger.CharterSeatId.HasValue && activeSeatIds.Contains(passenger.CharterSeatId.Value)
                && assignedSeatIds.Add(passenger.CharterSeatId.Value))
            {
                continue;
            }

            passenger.CharterSeatId = null;
            passenger.CharterSeat = null;
            passenger.TripId = null;
            passenger.TripSeatId = null;
            passenger.TripSeat = null;
        }

        if (passengers.Count > activeSeats.Count)
        {
            throw new ValidationException([new ValidationFailure("passengers",
                "Không đủ ghế đang hoạt động trên các tàu charter đã chọn.")]);
        }

        // Fill boat 1 before boat 2 and keep the configured Deck/Row/Column order.
        // Existing assignments stay fixed; newly approved passengers receive the next seat.
        foreach (var charterBoat in charterBoats)
        {
            var availableSeats = seatsByBoatId.GetValueOrDefault(charterBoat.BoatId, [])
                .Where(x => !assignedSeatIds.Contains(x.Id))
                .ToList();

            foreach (var passenger in passengers.Where(x => !x.CharterSeatId.HasValue).Take(availableSeats.Count).ToList())
            {
                var seat = availableSeats[0];
                availableSeats.RemoveAt(0);
                passenger.CharterSeatId = seat.Id;
                passenger.CharterSeat = seat;
                assignedSeatIds.Add(seat.Id);
            }
        }

        if (passengers.Any(x => !x.CharterSeatId.HasValue))
        {
            throw new ValidationException([new ValidationFailure("passengers",
                "Không thể phân ghế cho tất cả hành khách charter.")]);
        }

        await MapReservationsToTripsAsync(context, charterBoats, passengers, cancellationToken);
    }

    private static async Task MapReservationsToTripsAsync(
        IApplicationDbContext context,
        IReadOnlyList<CharterBookingBoat> charterBoats,
        IReadOnlyList<BookingPassenger> passengers,
        CancellationToken cancellationToken)
    {
        if (charterBoats.Any(x => !x.TripId.HasValue))
        {
            return;
        }

        var tripIds = charterBoats.Select(x => x.TripId!.Value).ToArray();
        var tripSeats = await context.Set<TripSeat>()
            .Include(x => x.Seat)
            .Where(x => tripIds.Contains(x.TripId))
            .ToListAsync(cancellationToken);
        var knownTripSeats = tripSeats
            .GroupBy(x => (x.TripId, x.SeatId))
            .ToDictionary(x => x.Key, x => x.First());
        var tripSeatIds = tripSeats.Select(x => x.Id).ToArray();
        var occupiedByOtherBookings = await context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => x.BookingId != passengers[0].BookingId
                && x.TripSeatId.HasValue
                && tripSeatIds.Contains(x.TripSeatId.Value))
            .Select(x => x.TripSeatId!.Value)
            .ToListAsync(cancellationToken);
        var externallyOccupiedTripSeatIds = occupiedByOtherBookings.ToHashSet();

        foreach (var tripSeat in tripSeats.Where(x => x.Status != TripSeat.StatusBlocked))
        {
            tripSeat.Status = externallyOccupiedTripSeatIds.Contains(tripSeat.Id)
                ? TripSeat.StatusBooked
                : TripSeat.StatusAvailable;
        }

        var boatIds = charterBoats.Select(x => x.BoatId).ToArray();
        var seats = await context.Seats
            .Where(x => boatIds.Contains(x.BoatId) && x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var tripByBoatId = charterBoats.ToDictionary(x => x.BoatId, x => x.TripId!.Value);

        foreach (var charterBoat in charterBoats)
        {
            foreach (var seat in seats.Values.Where(x => x.BoatId == charterBoat.BoatId))
            {
                var key = (charterBoat.TripId!.Value, seat.Id);
                if (knownTripSeats.ContainsKey(key))
                {
                    continue;
                }

                var tripSeat = new TripSeat
                {
                    TripId = key.Item1,
                    SeatId = seat.Id,
                    Seat = seat,
                    Status = TripSeat.StatusAvailable
                };
                context.Set<TripSeat>().Add(tripSeat);
                knownTripSeats[key] = tripSeat;
            }
        }

        foreach (var passenger in passengers)
        {
            var seat = seats[passenger.CharterSeatId!.Value];
            var tripId = tripByBoatId[seat.BoatId];
            var key = (tripId, seat.Id);
            if (!knownTripSeats.TryGetValue(key, out var tripSeat))
            {
                tripSeat = new TripSeat
                {
                    TripId = tripId,
                    SeatId = seat.Id,
                    Seat = seat,
                    Status = TripSeat.StatusAvailable
                };
                context.Set<TripSeat>().Add(tripSeat);
                knownTripSeats[key] = tripSeat;
            }

            if (tripSeat.Status == TripSeat.StatusBlocked
                || externallyOccupiedTripSeatIds.Contains(tripSeat.Id))
            {
                throw new ValidationException([new ValidationFailure("passengers",
                    $"Ghế {seat.Code} đã bị khóa hoặc được hành khách khác sử dụng.")]);
            }

            passenger.TripId = tripId;
            passenger.TripSeatId = tripSeat.Id;
            passenger.TripSeat = tripSeat;
            tripSeat.Status = TripSeat.StatusBooked;
        }
    }
}
