using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class FareCalculator : IFareCalculator
{
    private readonly IApplicationDbContext _context;

    public FareCalculator(IApplicationDbContext context) => _context = context;

    public async Task<decimal> CalculateAsync(
        Guid seatId,
        Guid ticketTypeId,
        CancellationToken cancellationToken)
    {
        var seat = await _context.Set<Seat>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == seatId, cancellationToken)
            ?? throw new NotFoundException($"Seat {seatId} not found.");

        if (!seat.IsActive)
            throw new NotFoundException($"Seat {seat.Code} is not active.");

        var ticketType = TicketTypeCatalog.FindActiveById(ticketTypeId)
            ?? throw new NotFoundException($"Ticket type {ticketTypeId} not found or inactive.");

        if (!SeatTypePricing.TryGetBasePrice(seat.SeatTypeCode, out var basePrice))
            throw new NotFoundException($"Seat type {seat.SeatTypeCode} does not have a valid price.");

        return basePrice * ticketType.PriceModifier;
    }
}
