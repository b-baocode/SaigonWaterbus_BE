using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Fares;

public sealed record GetFareQuery(
    Guid RouteId,
    Guid FromStationId,
    Guid ToStationId) : IRequest<IReadOnlyList<FareByTicketTypeDto>>;

public sealed class GetFareQueryHandler : IRequestHandler<GetFareQuery, IReadOnlyList<FareByTicketTypeDto>>
{
    private readonly IApplicationDbContext _context;

    public GetFareQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<FareByTicketTypeDto>> Handle(GetFareQuery request, CancellationToken cancellationToken)
    {
        var basePrice = await _context.Set<FareMatrix>()
            .Where(f => f.RouteId == request.RouteId
                     && f.FromStationId == request.FromStationId
                     && f.ToStationId == request.ToStationId
                     && f.IsActive)
            .Select(f => (decimal?)f.BasePrice)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("No active fare defined for this station pair.");

        var ticketTypes = await _context.Set<TicketType>()
            .Where(tt => tt.IsActive)
            .OrderBy(tt => tt.TicketTypeCode)
            .ToListAsync(cancellationToken);

        return ticketTypes.Select(tt => new FareByTicketTypeDto(
            tt.Id, tt.TicketTypeName,
            basePrice, tt.PriceModifier,
            basePrice * tt.PriceModifier)).ToList();
    }
}
