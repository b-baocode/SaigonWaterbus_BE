using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Fares;

public sealed record GetFareQuery(
    Guid BoatId,
    string SeatNumber) : IRequest<IReadOnlyList<FareByTicketTypeDto>>;

public sealed class GetFareQueryHandler : IRequestHandler<GetFareQuery, IReadOnlyList<FareByTicketTypeDto>>
{
    private readonly IApplicationDbContext _context;

    public GetFareQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<FareByTicketTypeDto>> Handle(GetFareQuery request, CancellationToken cancellationToken)
    {
        var seatCode = request.SeatNumber.Trim().ToUpperInvariant();
        var seat = await _context.Set<Seat>()
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.BoatId == request.BoatId && s.Code == seatCode, cancellationToken)
            ?? throw new NotFoundException($"Seat '{seatCode}' not found.");

        if (!seat.IsActive || !SeatTypePricing.TryGetBasePrice(seat.SeatTypeCode, out var basePrice))
            throw new NotFoundException($"Seat '{seatCode}' is not available for fare calculation.");

        return TicketTypeCatalog.ActiveDefinitions.Select(tt => new FareByTicketTypeDto(
            tt.TicketTypeId,
            tt.TicketTypeCode,
            tt.TicketTypeName,
            basePrice,
            tt.PriceModifier,
            basePrice * tt.PriceModifier)).ToList();
    }
}
