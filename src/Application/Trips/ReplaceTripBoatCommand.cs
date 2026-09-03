using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin,Manager")]
public sealed record ReplaceTripBoatCommand(Guid TripId, Guid BoatId) : IRequest<TripDetailDto>;

public sealed class ReplaceTripBoatCommandValidator : AbstractValidator<ReplaceTripBoatCommand>
{
    public ReplaceTripBoatCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.BoatId).NotEmpty();
    }
}

public sealed class ReplaceTripBoatCommandHandler : IRequestHandler<ReplaceTripBoatCommand, TripDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICharterBookingRealtimeNotifier _charterBookingRealtimeNotifier;

    public ReplaceTripBoatCommandHandler(
        IApplicationDbContext context,
        ICharterBookingRealtimeNotifier? charterBookingRealtimeNotifier = null)
    {
        _context = context;
        _charterBookingRealtimeNotifier = charterBookingRealtimeNotifier
            ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<TripDetailDto> Handle(ReplaceTripBoatCommand request, CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
                .ThenInclude(x => x.Station)
            .Include(x => x.TripSeats)
                .ThenInclude(x => x.Seat)
            .SingleOrDefaultAsync(x => x.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        if (trip.TripStatus is TripStatus.Completed or TripStatus.Cancelled)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.TripId),
                "Chỉ được thay tàu cho trip chưa Completed/Cancelled.")]);
        }

        var boat = await _context.Set<Boat>()
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tàu thay thế.");

        if (boat.Status != BoatStatus.Active || !boat.SeatsConfigured)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BoatId),
                "Tàu thay thế phải Active và đã setup ghế.")]);
        }

        if (!BoatRouteCompatibilitySupport.IsCompatible(trip.Route.RouteType, boat.SeatSetupType))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BoatId),
                BoatRouteCompatibilitySupport.BuildIncompatibleMessage(trip.Route.RouteType, boat.SeatSetupType))]);
        }

        var linkedCharterBoat = await _context.Set<CharterBookingBoat>()
            .Include(x => x.Booking)
            .SingleOrDefaultAsync(x => x.TripId == trip.Id, cancellationToken);
        var sourceBooking = linkedCharterBoat?.Booking;
        if (sourceBooking is null && trip.SourceBookingId.HasValue)
        {
            sourceBooking = await _context.Set<Booking>()
                .SingleOrDefaultAsync(
                    x => x.Id == trip.SourceBookingId.Value
                        && x.BookingType == Booking.CharterBookingType,
                    cancellationToken);
        }

        if (trip.BoatId == request.BoatId)
        {
            var synchronized = SynchronizeCharterReferences(
                trip,
                boat,
                linkedCharterBoat,
                sourceBooking);
            if (sourceBooking is not null)
            {
                var charterPassengers = await _context.Set<BookingPassenger>()
                    .Include(x => x.TripSeat)
                        .ThenInclude(x => x!.Seat)
                    .Where(x => x.BookingId == sourceBooking.Id
                        && x.TripId == trip.Id
                        && x.TripSeatId.HasValue)
                    .ToListAsync(cancellationToken);
                foreach (var passenger in charterPassengers.Where(x => x.TripSeat?.Seat is not null))
                {
                    var currentSeat = passenger.TripSeat!.Seat;
                    if (currentSeat.BoatId == boat.Id && passenger.CharterSeatId != currentSeat.Id)
                    {
                        passenger.CharterSeatId = currentSeat.Id;
                        passenger.CharterSeat = currentSeat;
                        synchronized = true;
                    }
                }
            }

            if (!synchronized)
            {
                throw new ValidationException([new ValidationFailure(nameof(request.BoatId),
                    "Tàu thay thế phải khác tàu hiện tại của trip.")]);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await PublishCharterBookingChangedAsync(sourceBooking!, cancellationToken);
            return UpdateTripStatusCommandHandler.ToDetailDto(trip);
        }

        var newBoatSeats = await _context.Set<Seat>()
            .Where(x => x.BoatId == boat.Id && x.IsActive)
            .OrderBy(x => x.Deck)
            .ThenBy(x => x.Row)
            .ThenBy(x => x.Column)
            .ToListAsync(cancellationToken);
        if (newBoatSeats.Count == 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BoatId),
                "Tàu thay thế chưa có ghế active.")]);
        }

        var activePassengerTripSeatIds = await _context.Set<BookingPassenger>()
            .Where(x => x.TripId == trip.Id && x.TripSeatId.HasValue)
            .Select(x => x.TripSeatId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var requiredSeatCodes = trip.TripSeats
            .Where(x => activePassengerTripSeatIds.Contains(x.Id))
            .Select(x => x.Seat.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var newSeatsByCode = newBoatSeats
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var missingSeatCodes = requiredSeatCodes
            .Where(code => !newSeatsByCode.ContainsKey(code))
            .ToList();
        if (missingSeatCodes.Count > 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BoatId),
                $"Tàu thay thế thiếu các mã ghế đang có vé: {string.Join(", ", missingSeatCodes)}.")]);
        }

        await EnsureReplacementBoatIsFreeAsync(trip, boat.Id, cancellationToken);

        var oldTripSeats = trip.TripSeats.ToList();
        var newTripSeats = newBoatSeats
            .Select(seat => new TripSeat
            {
                TripId = trip.Id,
                SeatId = seat.Id,
                Status = TripSeat.StatusAvailable,
                Price = null
            })
            .ToList();
        var newTripSeatsByOldCode = newTripSeats
            .Join(newBoatSeats, tripSeat => tripSeat.SeatId, seat => seat.Id, (tripSeat, seat) => new { tripSeat, seat.Code })
            .ToDictionary(x => x.Code, x => x.tripSeat, StringComparer.OrdinalIgnoreCase);

        var passengersToRemap = await _context.Set<BookingPassenger>()
            .Include(x => x.TripSeat)
                .ThenInclude(x => x!.Seat)
            .Where(x => x.TripId == trip.Id && x.TripSeatId.HasValue)
            .ToListAsync(cancellationToken);
        foreach (var passenger in passengersToRemap)
        {
            if (passenger.TripSeat?.Seat is null)
            {
                continue;
            }

            var seatCode = passenger.TripSeat.Seat.Code;
            var replacementTripSeat = newTripSeatsByOldCode[seatCode];
            passenger.TripSeatId = replacementTripSeat.Id;
            passenger.TripSeat = replacementTripSeat;
            if (sourceBooking is not null && passenger.BookingId == sourceBooking.Id)
            {
                var replacementSeat = newSeatsByCode[seatCode];
                passenger.CharterSeatId = replacementSeat.Id;
                passenger.CharterSeat = replacementSeat;
            }
        }

        _context.Set<TripSeat>().AddRange(newTripSeats);
        _context.Set<TripSeat>().RemoveRange(oldTripSeats);
        trip.BoatId = boat.Id;
        trip.Boat = boat;
        trip.CapacitySnapshot = newBoatSeats.Count;

        SynchronizeCharterReferences(trip, boat, linkedCharterBoat, sourceBooking);

        await _context.SaveChangesAsync(cancellationToken);

        if (sourceBooking is not null)
        {
            await PublishCharterBookingChangedAsync(sourceBooking, cancellationToken);
        }

        return UpdateTripStatusCommandHandler.ToDetailDto(trip);
    }

    private static bool SynchronizeCharterReferences(
        Trip trip,
        Boat boat,
        CharterBookingBoat? linkedCharterBoat,
        Booking? sourceBooking)
    {
        var changed = false;
        if (linkedCharterBoat is not null)
        {
            changed = linkedCharterBoat.BoatId != boat.Id
                || linkedCharterBoat.SeatSetupType != boat.SeatSetupType;
            linkedCharterBoat.BoatId = boat.Id;
            linkedCharterBoat.Boat = boat;
            linkedCharterBoat.SeatSetupType = boat.SeatSetupType;
        }

        if (sourceBooking is not null
            && (sourceBooking.TripId == trip.Id || linkedCharterBoat?.BoatOrder == 1))
        {
            changed |= sourceBooking.BoatId != boat.Id;
            sourceBooking.BoatId = boat.Id;
            sourceBooking.Boat = boat;
        }

        return changed;
    }

    private Task PublishCharterBookingChangedAsync(
        Booking booking,
        CancellationToken cancellationToken) =>
        _charterBookingRealtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "BoatReplaced",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus),
            cancellationToken);

    private async Task EnsureReplacementBoatIsFreeAsync(
        Trip trip,
        Guid boatId,
        CancellationToken cancellationToken)
    {
        var routeStops = trip.Route.RouteStops.OrderBy(x => x.StopOrder).ToList();
        var requestedWindow = new TripScheduleSupport.BoatScheduleWindow(
            trip.TripCode,
            trip.DepartureTime,
            trip.ArrivalTime,
            TripScheduleSupport.ResolveStartStationId(routeStops),
            TripScheduleSupport.ResolveEndStationId(routeStops),
            routeStops);

        var existingWindows = (await _context.Set<Trip>()
                .AsNoTracking()
                .Include(x => x.Route).ThenInclude(x => x.RouteStops)
                .Where(x => x.Id != trip.Id
                    && x.BoatId == boatId
                    && x.TripStatus != TripStatus.Cancelled
                    && x.ArrivalTime >= trip.DepartureTime.AddHours(-24)
                    && x.DepartureTime <= trip.ArrivalTime.AddHours(24))
                .ToListAsync(cancellationToken))
            .Where(x => x.Route.RouteStops.Count >= 2)
            .Select(x =>
            {
                var existingRouteStops = x.Route.RouteStops.OrderBy(stop => stop.StopOrder).ToList();
                return new TripScheduleSupport.BoatScheduleWindow(
                    x.TripCode,
                    x.DepartureTime,
                    x.ArrivalTime,
                    TripScheduleSupport.ResolveStartStationId(existingRouteStops),
                    TripScheduleSupport.ResolveEndStationId(existingRouteStops),
                    existingRouteStops);
            })
            .ToList();

        var conflict = TripScheduleSupport.FindConflict(requestedWindow, existingWindows);
        if (conflict is not null)
        {
            throw new ValidationException([new ValidationFailure(nameof(ReplaceTripBoatCommand.BoatId),
                "Tàu thay thế đã có chuyến vào ngày/giờ này: "
                + TripScheduleSupport.BuildLocationAwareConflictMessage(
                    conflict.Existing.TripCode,
                    conflict.Existing.DepartureTime,
                    conflict.Existing.ArrivalTime,
                    conflict.EarliestAllowedDeparture,
                    conflict.RepositionDuration))]);
        }
    }
}
