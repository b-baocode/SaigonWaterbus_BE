using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Seats;
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
        Guid ticketTypeId,
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

        var ticketType = await _context.Set<TicketType>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == ticketTypeId && x.IsActive, cancellationToken)
            ?? throw new NotFoundException($"Ticket type {ticketTypeId} not found or inactive.");

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

        var basePrice = await ResolveBasePriceAsync(seat, tripId, cancellationToken);
        return basePrice * ticketType.PriceModifier;
    }

    private async Task<decimal> ResolveBasePriceAsync(Seat seat, Guid? tripId, CancellationToken cancellationToken)
    {
        if (tripId.HasValue)
        {
            var tripSeat = await _context.Set<TripSeat>()
                .AsNoTracking()
                .FirstOrDefaultAsync(ts => ts.TripId == tripId.Value && ts.SeatId == seat.Id, cancellationToken);

            if (tripSeat?.Price is > 0)
                return tripSeat.Price.Value;
        }

        return seat.SeatType?.BasePrice ?? SeatTypePricing.GetBasePrice(seat.SeatTypeCode);
    }
}
