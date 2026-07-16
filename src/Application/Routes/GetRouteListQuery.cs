using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Routes;

public sealed record GetRouteListQuery(string? Usage = null) : IRequest<IReadOnlyList<RouteDto>>;

public sealed class GetRouteListQueryHandler : IRequestHandler<GetRouteListQuery, IReadOnlyList<RouteDto>>
{
    private readonly IApplicationDbContext _context;

    public GetRouteListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<RouteDto>> Handle(GetRouteListQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Set<Route>()
            .Where(r => r.Status == "Active");

        if (IsCharterSourceUsage(request.Usage))
        {
            query = query.Where(r =>
                r.RouteType == RouteTypes.CharterReference
                || r.RouteType == RouteTypes.SightseeingLoop);
        }

        return await query
            .OrderBy(r => r.RouteCode)
            .Select(r => new RouteDto(r.Id, r.RouteCode, r.RouteName, r.RouteType,
                r.Description, r.BaseDistanceKm, r.EstimatedDurationMin, r.Status,
                RoutePresentationSupport.ResolveLabel(r.RouteType),
                r.Status == "Active"
                    && (r.RouteType == RouteTypes.CharterReference || r.RouteType == RouteTypes.SightseeingLoop),
                r.RouteType == RouteTypes.Charter))
            .ToListAsync(cancellationToken);
    }

    private static bool IsCharterSourceUsage(string? usage) =>
        string.Equals(usage, "charter-source", StringComparison.OrdinalIgnoreCase)
        || string.Equals(usage, "charterSource", StringComparison.OrdinalIgnoreCase);
}
