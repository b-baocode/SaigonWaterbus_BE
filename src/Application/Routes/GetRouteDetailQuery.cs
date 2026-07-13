using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Routes;

public sealed record GetRouteDetailQuery(Guid RouteId) : IRequest<RouteDetailDto>;

public sealed class GetRouteDetailQueryHandler : IRequestHandler<GetRouteDetailQuery, RouteDetailDto>
{
    private readonly IApplicationDbContext _context;

    public GetRouteDetailQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<RouteDetailDto> Handle(GetRouteDetailQuery request, CancellationToken cancellationToken)
    {
        var route = await _context.Set<Route>()
            .Include(r => r.RouteStops)
                .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(r => r.Id == request.RouteId, cancellationToken)
            ?? throw new NotFoundException("Route not found.");

        var stops = route.RouteStops
            .OrderBy(rs => rs.StopOrder)
            .Select(rs => new RouteStopDto(
                rs.Id, rs.Station.Id, rs.Station.StationCode, rs.Station.StationName,
                rs.StopOrder, rs.StandardTravelMin,
                rs.IsPickupAllowed, rs.IsDropoffAllowed))
            .ToList();

        var geometry = route.RouteGeometry is null
            ? null
            : route.RouteGeometry.Coordinates
                .Select(c => new double[] { c.X, c.Y })
                .ToList();

        return new RouteDetailDto(route.Id, route.RouteCode, route.RouteName, route.RouteType,
            route.Description, route.BaseDistanceKm, route.EstimatedDurationMin, route.Status,
            stops, geometry);
    }
}
