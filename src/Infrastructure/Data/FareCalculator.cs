using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class FareCalculator : IFareCalculator
{
    private readonly IApplicationDbContext _context;

    public FareCalculator(IApplicationDbContext context) => _context = context;

    public async Task<decimal> CalculateAsync(
        Guid seatId,
        string ticketTypeCode,
        CancellationToken cancellationToken,
        Guid? tripId = null)
    {
        var seat = await _context.Set<Seat>()
            .AsNoTracking()
            .Include(x => x.SeatType)
            .SingleOrDefaultAsync(x => x.Id == seatId, cancellationToken)
            ?? throw new NotFoundException($"Seat {seatId} not found.");

        if (!seat.IsActive)
            throw new NotFoundException($"Seat {seat.Code} is not active.");

        if (!TicketTypePricing.TryGet(ticketTypeCode, out var ticketType))
            throw new NotFoundException($"Ticket type '{ticketTypeCode}' not found or inactive.");

        if (!ticketType.IsApplicableForSeatType(seat.SeatTypeCode))
        {
            var allowed = ticketType.AllowedSeatTypeCodes!;
            throw new ValidationException(
            [
                new ValidationFailure("ticketTypeCode",
                    $"Loại vé '{ticketType.Name}' chỉ áp dụng cho ghế: {allowed}. " +
                    $"Ghế '{seat.Code}' là loại {seat.SeatTypeCode}.")
            ]);
        }

        var tripPricingContext = await ResolveTripPricingContextAsync(tripId, cancellationToken);
        var basePrice = await ResolveBasePriceAsync(seat, tripPricingContext?.OperatingDate, cancellationToken);
        var priceModifier = await TicketFareRuleSupport.GetEffectivePriceModifierAsync(
            _context,
            ticketType,
            tripPricingContext?.RouteType,
            cancellationToken);
        return PriceRoundingSupport.RoundFare(basePrice * priceModifier);
    }

    private async Task<decimal> ResolveBasePriceAsync(
        Seat seat,
        DateOnly? operatingDate,
        CancellationToken cancellationToken)
    {
        var basePrice = seat.SeatType?.BasePrice ?? SeatTypePricing.GetBasePrice(seat.SeatTypeCode);

        if (operatingDate.HasValue)
        {
            var adjustment = await FareAdjustmentSupport.GetEffectiveAdjustmentAsync(
                _context, operatingDate.Value, cancellationToken);
            return FareAdjustmentSupport.ApplySurcharge(basePrice, adjustment);
        }

        return basePrice;
    }

    private async Task<TripPricingContext?> ResolveTripPricingContextAsync(
        Guid? tripId,
        CancellationToken cancellationToken)
    {
        if (!tripId.HasValue)
        {
            return null;
        }

        var trip = await _context.Set<Trip>()
            .AsNoTracking()
            .Where(x => x.Id == tripId.Value)
            .Select(x => new { x.OperatingDate, x.RouteId })
            .SingleOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return null;
        }

        var routeType = trip.RouteId == Guid.Empty
            ? null
            : await _context.Set<Route>()
                .AsNoTracking()
                .Where(x => x.Id == trip.RouteId)
                .Select(x => x.RouteType)
                .SingleOrDefaultAsync(cancellationToken);

        return new TripPricingContext(trip.OperatingDate, routeType);
    }

    private sealed record TripPricingContext(DateOnly OperatingDate, string? RouteType);
}
