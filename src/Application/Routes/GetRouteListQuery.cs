using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Routes;

public sealed record GetRouteListQuery : IRequest<IReadOnlyList<RouteDto>>;

public sealed class GetRouteListQueryHandler : IRequestHandler<GetRouteListQuery, IReadOnlyList<RouteDto>>
{
    private readonly IApplicationDbContext _context;

    public GetRouteListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<RouteDto>> Handle(GetRouteListQuery request, CancellationToken cancellationToken)
    {
        return await _context.Set<Route>()
            .Where(r => r.Status == "Active")
            .OrderBy(r => r.RouteCode)
            .Select(r => new RouteDto(r.Id, r.RouteCode, r.RouteName, r.RouteType,
                r.Description, r.BaseDistanceKm, r.EstimatedDurationMin, r.Status, r.IsBookable))
            .ToListAsync(cancellationToken);
    }
}
