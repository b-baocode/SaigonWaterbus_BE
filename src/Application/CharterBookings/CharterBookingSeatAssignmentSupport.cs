using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

/// <summary>
/// Allocates one physical seat to every approved charter passenger. Boats are filled in
/// <see cref="CharterBookingBoat.BoatOrder"/> order; seat selection inside a boat is random.
/// </summary>
internal static class CharterBookingSeatAssignmentSupport
{
    public static async Task AssignApprovedPassengersAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        var charterBoats = await context.CharterBookingBoats
            .Where(x => x.BookingId == booking.Id)
            .OrderBy(x => x.BoatOrder)
            .ToListAsync(cancellationToken);

        // A manifest can be submitted before operations creates charter trips. Allocate only
        // once every selected boat has its own trip, so a passenger is never assigned to a
        // guessed primary boat.
        if (charterBoats.Count == 0 || charterBoats.Any(x => !x.TripId.HasValue))
        {
            return;
        }

        var tripIds = charterBoats.Select(x => x.TripId!.Value).ToArray();
        var boatIds = charterBoats.Select(x => x.BoatId).ToArray();
        var activeSeats = await context.Seats
            .Where(x => boatIds.Contains(x.BoatId) && x.IsActive)
            .OrderBy(x => x.Deck)
            .ThenBy(x => x.Row)
            .ThenBy(x => x.Column)
            .ToListAsync(cancellationToken);
        var seatsByBoatId = activeSeats
            .GroupBy(x => x.BoatId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var tripSeats = await context.Set<TripSeat>()
            .Include(x => x.Seat)
            .Where(x => tripIds.Contains(x.TripId))
            .ToListAsync(cancellationToken);
        var tripSeatsByTripAndSeat = tripSeats
            .GroupBy(x => (x.TripId, x.SeatId))
            .ToDictionary(x => x.Key, x => x.First());

        foreach (var charterBoat in charterBoats)
        {
            foreach (var seat in seatsByBoatId.GetValueOrDefault(charterBoat.BoatId, []))
            {
                var key = (charterBoat.TripId!.Value, seat.Id);
                if (tripSeatsByTripAndSeat.ContainsKey(key))
                {
                    continue;
                }

                var tripSeat = new TripSeat
                {
                    TripId = key.Item1,
                    SeatId = seat.Id,
                    Seat = seat,
                    Price = null
                };
                context.Set<TripSeat>().Add(tripSeat);
                tripSeats.Add(tripSeat);
                tripSeatsByTripAndSeat[key] = tripSeat;
            }
        }

        var approvedPassengers = booking.Passengers
            .Where(CharterBookingPassengerSupport.IsApproved)
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .ToList();
        if (approvedPassengers.Count == 0)
        {
            return;
        }

        var seatIds = tripSeats.Select(x => x.Id).ToArray();
        var occupiedByOtherBookings = await context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => x.BookingId != booking.Id
                && x.TripSeatId.HasValue
                && seatIds.Contains(x.TripSeatId.Value))
            .Select(x => x.TripSeatId!.Value)
            .ToListAsync(cancellationToken);
        var occupiedSeatIds = occupiedByOtherBookings.ToHashSet();

        // A full manifest replacement deletes the old passengers in the same unit of work.
        // Reset this charter's non-blocked seats before applying valid current assignments.
        foreach (var tripSeat in tripSeats.Where(x => x.Status != TripSeat.StatusBlocked))
        {
            tripSeat.Status = TripSeat.StatusAvailable;
        }

        foreach (var seatId in occupiedSeatIds)
        {
            var occupiedSeat = tripSeats.FirstOrDefault(x => x.Id == seatId);
            if (occupiedSeat is not null)
            {
                occupiedSeat.Status = TripSeat.StatusBooked;
            }
        }

        var seatsByTripId = charterBoats.ToDictionary(
            x => x.TripId!.Value,
            x => tripSeats
                .Where(seat => seat.TripId == x.TripId!.Value
                    && seat.Seat.IsActive
                    && seat.Status != TripSeat.StatusBlocked)
                .ToList());
        var assignableCapacity = seatsByTripId.Values
            .SelectMany(x => x)
            .Count(x => !occupiedSeatIds.Contains(x.Id));
        if (approvedPassengers.Count > assignableCapacity)
        {
            throw new ValidationException([new ValidationFailure("passengers",
                "Không đủ ghế đang hoạt động và không bị khóa trên các tàu charter đã tạo.")]);
        }

        foreach (var passenger in approvedPassengers)
        {
            if (passenger.TripId.HasValue
                && passenger.TripSeatId.HasValue
                && seatsByTripId.TryGetValue(passenger.TripId.Value, out var seats)
                && seats.Any(x => x.Id == passenger.TripSeatId.Value)
                && !occupiedSeatIds.Contains(passenger.TripSeatId.Value))
            {
                occupiedSeatIds.Add(passenger.TripSeatId.Value);
                var assignedSeat = tripSeats.Single(x => x.Id == passenger.TripSeatId.Value);
                passenger.TripSeat = assignedSeat;
                assignedSeat.Status = TripSeat.StatusBooked;
                continue;
            }

            passenger.TripId = null;
            passenger.TripSeatId = null;
            passenger.TripSeat = null;
        }

        foreach (var charterBoat in charterBoats)
        {
            var availableSeats = seatsByTripId[charterBoat.TripId!.Value]
                .Where(x => !occupiedSeatIds.Contains(x.Id))
                .ToList();
            Shuffle(availableSeats);

            foreach (var passenger in approvedPassengers.Where(x => !x.TripSeatId.HasValue).Take(availableSeats.Count))
            {
                var tripSeat = availableSeats[0];
                availableSeats.RemoveAt(0);
                passenger.TripId = charterBoat.TripId;
                passenger.TripSeatId = tripSeat.Id;
                passenger.TripSeat = tripSeat;
                tripSeat.Status = TripSeat.StatusBooked;
                occupiedSeatIds.Add(tripSeat.Id);
            }
        }

        if (approvedPassengers.Any(x => !x.TripSeatId.HasValue))
        {
            throw new ValidationException([new ValidationFailure("passengers",
                "Không thể phân ghế cho tất cả hành khách charter.")]);
        }
    }

    private static void Shuffle<T>(IList<T> values)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }
}
