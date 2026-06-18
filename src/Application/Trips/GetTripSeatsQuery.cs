using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

public sealed record GetTripSeatsQuery(Guid TripId) : IRequest<TripSeatMapDto>;

// ── DTOs ────────────────────────────────────────────────────────────────────

public sealed record TripSeatMapDto(
    Guid TripId,
    string TripCode,
    int TotalSeats,
    int BookedSeats,
    int AvailableSeats,
    IReadOnlyList<TripSeatDeckDto> Decks);

public sealed record TripSeatDeckDto(
    int Deck,
    IReadOnlyList<TripSeatRowDto> Rows);

public sealed record TripSeatRowDto(
    string Row,
    IReadOnlyList<TripSeatItemDto> Seats);

public sealed record TripSeatItemDto(
    Guid TripSeatId,
    Guid SeatId,
    string SeatCode,
    int Deck,
    string Row,
    int Column,
    string? SeatTypeCode,
    string? SeatTypeName);

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class GetTripSeatsQueryHandler : IRequestHandler<GetTripSeatsQuery, TripSeatMapDto>
{
    private readonly IApplicationDbContext _context;

    public GetTripSeatsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<TripSeatMapDto> Handle(GetTripSeatsQuery request, CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .AsNoTracking()
            .Select(t => new { t.Id, t.TripCode })
            .SingleOrDefaultAsync(t => t.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException($"Trip '{request.TripId}' not found.");

        var tripSeats = await _context.Set<TripSeat>()
            .AsNoTracking()
            .Where(ts => ts.TripId == request.TripId)
            .Include(ts => ts.Seat)
                .ThenInclude(s => s.SeatType)
            .OrderBy(ts => ts.Seat.Deck)
                .ThenBy(ts => ts.Seat.Row)
                .ThenBy(ts => ts.Seat.Column)
            .ToListAsync(cancellationToken);

        var bookedCount = await _context.Set<BookingItem>()
            .AsNoTracking()
            .Where(bi => bi.TripId == request.TripId && bi.ItemStatus != BookingItemStatus.Cancelled)
            .CountAsync(cancellationToken);

        var seatItems = tripSeats.Select(ts => new TripSeatItemDto(
            ts.Id,
            ts.SeatId,
            ts.Seat.Code,
            ts.Seat.Deck,
            ts.Seat.Row,
            ts.Seat.Column,
            ts.Seat.SeatType?.Code,
            ts.Seat.SeatType?.Name)).ToList();

        var decks = seatItems
            .GroupBy(s => s.Deck)
            .OrderBy(g => g.Key)
            .Select(deckGroup => new TripSeatDeckDto(
                deckGroup.Key,
                deckGroup
                    .GroupBy(s => s.Row)
                    .OrderBy(g => g.Key)
                    .Select(rowGroup => new TripSeatRowDto(
                        rowGroup.Key,
                        rowGroup.OrderBy(s => s.Column).ToList()))
                    .ToList()))
            .ToList();

        return new TripSeatMapDto(
            trip.Id,
            trip.TripCode,
            TotalSeats: seatItems.Count,
            BookedSeats: bookedCount,
            AvailableSeats: Math.Max(0, seatItems.Count - bookedCount),
            Decks: decks);
    }
}
