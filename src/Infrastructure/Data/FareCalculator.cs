using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class FareCalculator : IFareCalculator
{
    private readonly IApplicationDbContext _context;

    public FareCalculator(IApplicationDbContext context) => _context = context;

    public async Task<decimal> CalculateAsync(
        Guid routeId,
        Guid fromStationId,
        Guid toStationId,
        Guid ticketTypeId,
        CancellationToken cancellationToken)
    {
        var (basePrice, ticketModifier) = await GetBaseComponentsAsync(
            routeId,
            fromStationId,
            toStationId,
            ticketTypeId,
            cancellationToken);

        return basePrice * ticketModifier;
    }

    public async Task<decimal> CalculateAsync(
        Guid routeId,
        Guid fromStationId,
        Guid toStationId,
        Guid ticketTypeId,
        Guid waterbusServiceId,
        Guid seatTypeId,
        CancellationToken cancellationToken)
    {
        var (basePrice, ticketModifier) = await GetBaseComponentsAsync(
            routeId,
            fromStationId,
            toStationId,
            ticketTypeId,
            cancellationToken);

        var seatModifier = await _context.ServiceSeatTypePrices
            .Where(x => x.WaterbusServiceId == waterbusServiceId
                     && x.SeatTypeId == seatTypeId
                     && x.IsActive
                     && x.SeatType.IsActive)
            .Select(x => (decimal?)x.PriceModifier)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(
                $"No active seat price is configured for service {waterbusServiceId} and seat type {seatTypeId}.");

        return basePrice * ticketModifier * seatModifier;
    }

    private async Task<(decimal BasePrice, decimal TicketModifier)> GetBaseComponentsAsync(
        Guid routeId,
        Guid fromStationId,
        Guid toStationId,
        Guid ticketTypeId,
        CancellationToken cancellationToken)
    {
        var basePrice = await _context.Set<FareMatrix>()
            .Where(f => f.RouteId == routeId
                     && f.FromStationId == fromStationId
                     && f.ToStationId == toStationId
                     && f.IsActive)
            .Select(f => (decimal?)f.BasePrice)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"No active fare defined for this station pair on route {routeId}.");

        var modifier = await _context.Set<TicketType>()
            .Where(tt => tt.Id == ticketTypeId && tt.IsActive)
            .Select(tt => (decimal?)tt.PriceModifier)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Ticket type {ticketTypeId} not found or inactive.");

        return (basePrice, modifier);
    }
}
