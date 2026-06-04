using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Fares;

public sealed record GetFareQuery(
    string RouteCode,
    string FromStationCode,
    string ToStationCode) : IRequest<IReadOnlyList<FareByTicketTypeDto>>;

public sealed class GetFareQueryHandler : IRequestHandler<GetFareQuery, IReadOnlyList<FareByTicketTypeDto>>
{
    private readonly IApplicationDbContext _context;

    public GetFareQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<FareByTicketTypeDto>> Handle(GetFareQuery request, CancellationToken cancellationToken)
    {
        var routeCode = request.RouteCode.Trim().ToUpperInvariant();
        var route = await _context.Set<Route>()
            .SingleOrDefaultAsync(r => r.RouteCode == routeCode, cancellationToken)
            ?? throw new NotFoundException($"Route '{routeCode}' not found.");

        var fromCode = request.FromStationCode.Trim().ToUpperInvariant();
        var fromStation = await _context.Set<Station>()
            .SingleOrDefaultAsync(s => s.StationCode == fromCode, cancellationToken)
            ?? throw new NotFoundException($"Station '{fromCode}' not found.");

        var toCode = request.ToStationCode.Trim().ToUpperInvariant();
        var toStation = await _context.Set<Station>()
            .SingleOrDefaultAsync(s => s.StationCode == toCode, cancellationToken)
            ?? throw new NotFoundException($"Station '{toCode}' not found.");

        var basePrice = await _context.Set<FareMatrix>()
            .Where(f => f.RouteId == route.Id
                     && f.FromStationId == fromStation.Id
                     && f.ToStationId == toStation.Id
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
