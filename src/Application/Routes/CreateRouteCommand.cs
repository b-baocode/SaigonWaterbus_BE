using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

public sealed record CreateRouteCommand(
    string RouteCode,
    string RouteName,
    string? Description,
    int? EstimatedDurationMin,
    IReadOnlyList<CreateRouteWaypointDto> Waypoints,
    bool AutoRouteGeometry = false,
    string? PreferWaterwayType = null) : IRequest<RouteDto>;

public sealed record CreateRouteWaypointDto(
    string Type,
    string? StationCode,
    string? WaterwayOsmId,
    int? StopOrder = null);

public sealed class CreateRouteCommandValidator : AbstractValidator<CreateRouteCommand>
{
    public CreateRouteCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.RouteName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.EstimatedDurationMin).GreaterThan(0).When(x => x.EstimatedDurationMin.HasValue);
        RuleFor(x => x.PreferWaterwayType)
            .Must(t => t == null || t == "river" || t == "canal" || t == "custom")
            .WithMessage("PreferWaterwayType phai la 'river', 'canal', hoac 'custom'.")
            .When(x => x.PreferWaterwayType != null);

        RuleFor(x => x.Waypoints)
            .NotNull()
            .Must(HaveMinimumRequiredWaypoints)
            .WithMessage("Waypoints must contain at least 2 station waypoints.")
            .Must(HaveStationAtBothEnds)
            .WithMessage("The first and last waypoint must be station waypoints.")
            .Must(HaveConsistentStopOrders)
            .WithMessage("Either all station waypoints must specify StopOrder, or none must.")
            .Must(HaveUniqueStopOrders)
            .WithMessage("StopOrder values must be unique across all station waypoints.");

        RuleForEach(x => x.Waypoints)
            .SetValidator(new CreateRouteWaypointDtoValidator());
    }

    private static bool HaveMinimumRequiredWaypoints(IReadOnlyList<CreateRouteWaypointDto>? waypoints) =>
        waypoints is not null
        && waypoints.Count(IsStationWaypoint) >= 2;

    private static bool HaveStationAtBothEnds(IReadOnlyList<CreateRouteWaypointDto>? waypoints) =>
        waypoints is not null
        && waypoints.Count >= 2
        && IsStationWaypoint(waypoints[0])
        && IsStationWaypoint(waypoints[^1]);

    private static bool HaveConsistentStopOrders(IReadOnlyList<CreateRouteWaypointDto>? waypoints)
    {
        if (waypoints is null) return true;
        var stations = waypoints.Where(IsStationWaypoint).ToList();
        var withOrder = stations.Count(x => x.StopOrder.HasValue);
        return withOrder == 0 || withOrder == stations.Count;
    }

    private static bool HaveUniqueStopOrders(IReadOnlyList<CreateRouteWaypointDto>? waypoints)
    {
        if (waypoints is null) return true;
        var orders = waypoints
            .Where(x => IsStationWaypoint(x) && x.StopOrder.HasValue)
            .Select(x => x.StopOrder!.Value)
            .ToList();
        return orders.Count == orders.Distinct().Count();
    }

    private static bool IsStationWaypoint(CreateRouteWaypointDto waypoint) =>
        string.Equals(waypoint.Type, WaypointTypes.Station, StringComparison.OrdinalIgnoreCase);
}

public sealed class CreateRouteCommandHandler : IRequestHandler<CreateRouteCommand, RouteDto>
{
    private readonly IApplicationDbContext _context;

    public CreateRouteCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<RouteDto> Handle(CreateRouteCommand request, CancellationToken cancellationToken)
    {
        var code = request.RouteCode.Trim().ToUpperInvariant();

        if (await _context.Set<Route>().AnyAsync(r => r.RouteCode == code, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.RouteCode), "Route code already exists.")]);
        }

        var hasViaWaterway = request.Waypoints.Any(waypoint =>
            string.Equals(waypoint.Type, WaypointTypes.ViaWaterway, StringComparison.OrdinalIgnoreCase));
        var buildGeometry = hasViaWaterway || request.AutoRouteGeometry;

        var waterwaySegments = buildGeometry
            ? await _context.Set<WaterwaySegment>()
                .AsNoTracking()
                .OrderBy(segment => segment.OsmId)
                .ThenBy(segment => segment.SegmentOrder)
                .ToListAsync(cancellationToken)
            : [];

        if (buildGeometry)
        {
            var filtered = string.IsNullOrWhiteSpace(request.PreferWaterwayType)
                ? waterwaySegments
                : waterwaySegments
                    .Where(s => string.Equals(s.WaterwayType, request.PreferWaterwayType.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                    .ToList();

            if (filtered.Count == 0)
            {
                var detail = string.IsNullOrWhiteSpace(request.PreferWaterwayType)
                    ? "No waterway network has been imported yet. Import GeoJSON map data first or create the route with station waypoints only."
                    : $"No waterway segments of type '{request.PreferWaterwayType}' found. Import GeoJSON map data with the correct waterway type first.";
                throw new ValidationException([new ValidationFailure(nameof(request.Waypoints), detail)]);
            }

            waterwaySegments = filtered;
        }

        var stationCodes = request.Waypoints
            .Where(waypoint => string.Equals(waypoint.Type, WaypointTypes.Station, StringComparison.OrdinalIgnoreCase))
            .Select(waypoint => waypoint.StationCode!.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var stationsByCode = await _context.Set<Station>()
            .Where(station => stationCodes.Contains(station.StationCode))
            .ToDictionaryAsync(station => station.StationCode, cancellationToken);

        var useExplicitStopOrder = request.Waypoints
            .Any(w => string.Equals(w.Type, WaypointTypes.Station, StringComparison.OrdinalIgnoreCase) && w.StopOrder.HasValue);

        var routeStationPairs = new List<(Station Station, int StopOrder)>();
        var waypointPoints = new List<Point>();
        var autoStopOrderCounter = 0;

        foreach (var waypoint in request.Waypoints)
        {
            if (string.Equals(waypoint.Type, WaypointTypes.Station, StringComparison.OrdinalIgnoreCase))
            {
                var stationCode = waypoint.StationCode!.Trim().ToUpperInvariant();
                if (!stationsByCode.TryGetValue(stationCode, out var station))
                {
                    throw new ValidationException([new ValidationFailure(
                        nameof(request.Waypoints),
                        $"Station '{stationCode}' was not found. Import the ferry terminal first or choose another station.")]);
                }

                autoStopOrderCounter++;
                var assignedStopOrder = useExplicitStopOrder ? waypoint.StopOrder!.Value : autoStopOrderCounter;
                routeStationPairs.Add((station, assignedStopOrder));

                if (buildGeometry)
                {
                    waypointPoints.Add(GetStationPoint(station, nameof(request.Waypoints)));
                }

                continue;
            }

            var waterwayRef = waypoint.WaterwayOsmId!.Trim();
            var viaGeometries = waterwaySegments
                .Where(segment => string.Equals(segment.OsmId, waterwayRef, StringComparison.OrdinalIgnoreCase))
                .OrderBy(segment => segment.SegmentOrder)
                .Select(segment => segment.Geometry)
                .ToList();

            if (viaGeometries.Count == 0)
            {
                viaGeometries = waterwaySegments
                    .Where(segment => string.Equals(segment.WaterwayName, waterwayRef, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(segment => segment.SegmentOrder)
                    .Select(segment => segment.Geometry)
                    .ToList();
            }

            if (viaGeometries.Count == 0)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.Waypoints),
                    $"Waterway '{waterwayRef}' was not found by OSM ID or name. Import the latest GeoJSON map or choose another via.")]);
            }

            waypointPoints.Add(RouteGeoJsonImportSupport.CreateRepresentativePoint(viaGeometries));
        }

        var routeGeometry = buildGeometry
            ? RouteGeoJsonImportSupport.BuildRouteGeometry(
                waterwaySegments.Select(segment => segment.Geometry).ToList(),
                waypointPoints)
            : null;
        var distanceKm = routeGeometry is null
            ? (decimal?)null
            : (decimal)Math.Round(RouteGeoJsonImportSupport.CalculateLengthKm(routeGeometry), 2);

        var route = new Route
        {
            RouteCode = code,
            RouteName = request.RouteName.Trim(),
            Description = request.Description?.Trim(),
            BaseDistanceKm = distanceKm,
            EstimatedDurationMin = request.EstimatedDurationMin,
            Status = "Active",
            RouteGeometry = routeGeometry
        };

        _context.Set<Route>().Add(route);

        for (var i = 0; i < routeStationPairs.Count; i++)
        {
            var (station, stopOrder) = routeStationPairs[i];
            _context.Set<RouteStop>().Add(new RouteStop
            {
                RouteId = route.Id,
                StationId = station.Id,
                StopOrder = stopOrder,
                IsPickupAllowed = i < routeStationPairs.Count - 1,
                IsDropoffAllowed = i > 0
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new RouteDto(
            route.Id,
            route.RouteCode,
            route.RouteName,
            route.Description,
            route.BaseDistanceKm,
            route.EstimatedDurationMin,
            route.Status);
    }

    private static Point GetStationPoint(Station station, string propertyName)
    {
        if (station.Location is not null)
        {
            return new Point(station.Location.X, station.Location.Y) { SRID = 4326 };
        }

        if (station.Latitude.HasValue && station.Longitude.HasValue)
        {
            return new Point((double)station.Longitude.Value, (double)station.Latitude.Value) { SRID = 4326 };
        }

        throw new ValidationException([new ValidationFailure(
            propertyName,
            $"Station '{station.StationCode}' does not have a valid location.")]);
    }
}

internal static class WaypointTypes
{
    public const string Station = "station";
    public const string ViaWaterway = "viaWaterway";
}

internal sealed class CreateRouteWaypointDtoValidator : AbstractValidator<CreateRouteWaypointDto>
{
    public CreateRouteWaypointDtoValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(type =>
                string.Equals(type, WaypointTypes.Station, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, WaypointTypes.ViaWaterway, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Waypoint type must be either 'station' or 'viaWaterway'.");

        When(x => string.Equals(x.Type, WaypointTypes.Station, StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.StationCode).NotEmpty().MaximumLength(50);
            RuleFor(x => x.WaterwayOsmId).Empty();
            RuleFor(x => x.StopOrder).GreaterThan(0).When(x => x.StopOrder.HasValue);
        });

        When(x => string.Equals(x.Type, WaypointTypes.ViaWaterway, StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.WaterwayOsmId).NotEmpty().MaximumLength(50);
            RuleFor(x => x.StationCode).Empty();
        });
    }
}
