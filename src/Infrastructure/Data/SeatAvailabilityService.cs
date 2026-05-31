using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class SeatAvailabilityService : ISeatAvailabilityService
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public SeatAvailabilityService(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<Guid>> GetAvailableSeatsAsync(
        Guid tripId,
        IEnumerable<Guid> requestedSeatIds,
        CancellationToken cancellationToken)
    {
        var seatList = requestedSeatIds.ToList();
        var now = _timeProvider.GetUtcNow();

        var heldSeatIds = await _context.Set<SeatHold>()
            .Where(sh => sh.TripId == tripId
                      && sh.HoldStatus == SeatHoldStatus.Active
                      && sh.ExpiresAt > now
                      && seatList.Contains(sh.SeatId))
            .Select(sh => sh.SeatId)
            .ToListAsync(cancellationToken);

        var bookedSeatIds = await _context.Set<BookingItem>()
            .Where(bi => bi.TripId == tripId
                      && bi.ItemStatus != BookingItemStatus.Cancelled
                      && bi.SeatId.HasValue
                      && seatList.Contains(bi.SeatId!.Value))
            .Select(bi => bi.SeatId!.Value)
            .ToListAsync(cancellationToken);

        var occupied = heldSeatIds.Union(bookedSeatIds).ToHashSet();
        return seatList.Where(id => !occupied.Contains(id)).ToList();
    }
}
