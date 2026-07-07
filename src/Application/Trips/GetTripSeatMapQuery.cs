using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

public sealed record TripSeatMapSeatDto(
    string SeatNumber,
    int Deck,
    string Row,
    int Column,
    string SeatTypeCode,
    string? SeatTypeName,
    decimal BasePrice,
    string Status);

public sealed record TripSeatMapDto(
    Guid TripId,
    string TripCode,
    Guid? BoatId,
    string? BoatName,
    DateTimeOffset DepartureTime,
    int TotalSeats,
    int AvailableSeats,
    int HoldTtlSeconds,
    IReadOnlyList<TripSeatMapSeatDto> Seats);

public sealed record GetTripSeatMapQuery(Guid TripId) : IRequest<TripSeatMapDto>;

public sealed class GetTripSeatMapQueryHandler : IRequestHandler<GetTripSeatMapQuery, TripSeatMapDto>
{
    public const string StatusAvailable = "Available";
    public const string StatusHeld = "Held";
    public const string StatusHeldByMe = "HeldByMe";
    public const string StatusBooked = "Booked";
    public const string StatusBlocked = "Blocked";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ISeatHoldService _seatHoldService;

    public GetTripSeatMapQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ISeatHoldService? seatHoldService = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _seatHoldService = seatHoldService ?? NullSeatHoldService.Instance;
    }

    public async Task<TripSeatMapDto> Handle(GetTripSeatMapQuery request, CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .AsNoTracking()
            .Include(t => t.Boat)
            .SingleOrDefaultAsync(t => t.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        if (!trip.BoatId.HasValue)
        {
            return new TripSeatMapDto(
                trip.Id, trip.TripCode, null, null, trip.DepartureTime,
                0, 0, (int)BookingSeatOccupancySupport.SeatPreHoldDuration.TotalSeconds, []);
        }

        var seats = await _context.Set<Seat>()
            .AsNoTracking()
            .Include(x => x.SeatType)
            .Where(x => x.BoatId == trip.BoatId.Value && x.IsActive)
            .ToListAsync(cancellationToken);

        var seatIds = seats.Select(x => x.Id).ToList();
        var tripSeatsBySeatId = await _context.Set<TripSeat>()
            .AsNoTracking()
            .Where(x => x.TripId == trip.Id && seatIds.Contains(x.SeatId))
            .ToDictionaryAsync(x => x.SeatId, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var tripSeatIds = tripSeatsBySeatId.Values.Select(x => x.Id).ToList();
        var occupiedTripSeatIds = (await _context.Set<BookingPassenger>()
                .Where(x => x.TripSeatId.HasValue && tripSeatIds.Contains(x.TripSeatId.Value))
                .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
                .Select(x => x.TripSeatId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var heldSeats = await _seatHoldService.GetHeldSeatsAsync(trip.Id, cancellationToken);
        var currentUserId = _userContext.UserId;

        var seatDtos = seats
            .OrderBy(x => x.Deck)
            .ThenBy(x => x.Row)
            .ThenBy(x => x.Column)
            .Select(seat =>
            {
                tripSeatsBySeatId.TryGetValue(seat.Id, out var tripSeat);
                return new TripSeatMapSeatDto(
                    seat.Code,
                    seat.Deck,
                    seat.Row,
                    seat.Column,
                    seat.SeatTypeCode,
                    seat.SeatType?.Name,
                    ResolveBasePrice(seat, tripSeat),
                    ResolveStatus(tripSeat, occupiedTripSeatIds, heldSeats, currentUserId));
            })
            .ToList();

        return new TripSeatMapDto(
            trip.Id,
            trip.TripCode,
            trip.BoatId,
            trip.Boat?.Name,
            trip.DepartureTime,
            seatDtos.Count,
            seatDtos.Count(x => x.Status == StatusAvailable),
            (int)BookingSeatOccupancySupport.SeatPreHoldDuration.TotalSeconds,
            seatDtos);
    }

    private static decimal ResolveBasePrice(Seat seat, TripSeat? tripSeat)
    {
        if (tripSeat?.Price is > 0)
        {
            return tripSeat.Price.Value;
        }

        if (seat.SeatType is not null)
        {
            return seat.SeatType.BasePrice;
        }

        return SeatTypePricing.TryGetBasePrice(seat.SeatTypeCode, out var basePrice) ? basePrice : 0;
    }

    private static string ResolveStatus(
        TripSeat? tripSeat,
        HashSet<Guid> occupiedTripSeatIds,
        IReadOnlyDictionary<Guid, Guid> heldSeats,
        Guid? currentUserId)
    {
        if (tripSeat is null || tripSeat.Status == TripSeat.StatusBlocked)
        {
            return StatusBlocked;
        }

        if (occupiedTripSeatIds.Contains(tripSeat.Id))
        {
            return StatusBooked;
        }

        if (heldSeats.TryGetValue(tripSeat.Id, out var holderUserId))
        {
            return holderUserId == currentUserId ? StatusHeldByMe : StatusHeld;
        }

        return StatusAvailable;
    }
}
