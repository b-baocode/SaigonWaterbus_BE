using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

public sealed record GetTripAvailableSeatsQuery(Guid TripId) : IRequest<SeatMapDto>;

public sealed class GetTripAvailableSeatsQueryHandler : IRequestHandler<GetTripAvailableSeatsQuery, SeatMapDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public GetTripAvailableSeatsQueryHandler(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<SeatMapDto> Handle(GetTripAvailableSeatsQuery request, CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .Include(t => t.Boat)
                .ThenInclude(b => b.Seats.Where(s => s.IsActive))
            .SingleOrDefaultAsync(t => t.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        var now = _timeProvider.GetUtcNow();

        var heldSeatIds = await _context.Set<SeatHold>()
            .Where(sh => sh.TripId == request.TripId
                      && sh.HoldStatus == SeatHoldStatus.Active
                      && sh.ExpiresAt > now)
            .Select(sh => sh.SeatId)
            .ToHashSetAsync(cancellationToken);

        var bookedSeatIds = await _context.Set<BookingItem>()
            .Where(bi => bi.TripId == request.TripId
                      && bi.ItemStatus != BookingItemStatus.Cancelled
                      && bi.SeatId.HasValue)
            .Select(bi => bi.SeatId!.Value)
            .ToHashSetAsync(cancellationToken);

        var seats = trip.Boat.Seats.OrderBy(s => s.SeatRow).ThenBy(s => s.SeatColumn).Select(s =>
        {
            var status = bookedSeatIds.Contains(s.Id) ? "Booked"
                       : heldSeatIds.Contains(s.Id) ? "Held"
                       : "Available";
            return new SeatStatusDto(s.Id, s.SeatNumber, s.SeatClass, s.SeatRow, s.SeatColumn, status);
        }).ToList();

        return new SeatMapDto(
            trip.Boat.Id, trip.Boat.BoatName,
            seats.Count,
            seats.Count(s => s.Status == "Available"),
            seats);
    }
}
