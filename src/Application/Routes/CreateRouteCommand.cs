using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

[Authorize(Roles = "Admin")]
public sealed record CreateRouteCommand(
    string RouteCode,
    string RouteName,
    string? Description,
    IReadOnlyList<CreateRouteWaypointDto> Waypoints,
    bool? AutoRouteGeometry = null,
    string? PreferWaterwayType = null,
    IReadOnlyList<string>? AvoidWaterwayOsmIds = null,
    IReadOnlyList<double[]>? ChosenGeometry = null,
    Guid? BoatId = null) : IRequest<RouteDto>;

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
        RuleFor(x => x.BoatId).NotEmpty().When(x => x.BoatId.HasValue);
        RuleFor(x => x.PreferWaterwayType)
            .Must(t => t == null || t == "river" || t == "canal" || t == "custom")
            .WithMessage("PreferWaterwayType phai la 'river', 'canal', hoac 'custom'.")
            .When(x => x.PreferWaterwayType != null);

        RuleForEach(x => x.AvoidWaterwayOsmIds!)
            .NotEmpty().WithMessage("avoidWaterwayOsmIds khong duoc chua phan tu rong.")
            .MaximumLength(50)
            .When(x => x.AvoidWaterwayOsmIds is not null);

        When(x => x.ChosenGeometry is not null, () =>
        {
            RuleFor(x => x.ChosenGeometry!)
                .Must(g => g.Count >= 2)
                .WithMessage("chosenGeometry phai co it nhat 2 diem.")
                .Must(g => g.All(c => c is { Length: >= 2 }
                    && c[0] is >= -180 and <= 180
                    && c[1] is >= -90 and <= 90))
                .WithMessage("chosenGeometry: moi diem phai la [longitude, latitude] hop le.");
            RuleFor(x => x.Waypoints)
                .Must(w => w is null || !w.Any(x2 =>
                    string.Equals(x2.Type, WaypointTypes.ViaWaterway, StringComparison.OrdinalIgnoreCase)))
                .WithMessage("Khong dung viaWaterway kem chosenGeometry - geometry da duoc chon san tu preview.");
        });

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
    // Van toc di chuyen thuc te lay 70% MaxSpeedKmh cua thuyen (tinh ca dung ben, giam toc khu dong duc).
    private const double RouteSpeedFactor = 0.7;

    private readonly IApplicationDbContext _context;

    public CreateRouteCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<RouteDto> Handle(CreateRouteCommand request, CancellationToken cancellationToken)
    {
        var code = request.RouteCode.Trim().ToUpperInvariant();

        if (await _context.Set<Route>().AnyAsync(r => r.RouteCode == code, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.RouteCode), "Route code already exists.")]);
        }

        // Neu chon thuyen: van toc uoc tinh = MaxSpeedKmh * 70%. Kiem tra som de bao loi truoc khi dung geometry.
        double? effectiveSpeedKmh = null;
        if (request.BoatId.HasValue)
        {
            var boat = await _context.Set<Boat>()
                .AsNoTracking()
                .SingleOrDefaultAsync(b => b.Id == request.BoatId.Value, cancellationToken)
                ?? throw new ValidationException([new ValidationFailure(
                    nameof(request.BoatId), "Boat not found.")]);

            if (!boat.MaxSpeedKmh.HasValue || boat.MaxSpeedKmh.Value <= 0)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.BoatId),
                    "Thuyen da chon chua co MaxSpeedKmh hop le de uoc tinh thoi gian di chuyen.")]);
            }

            effectiveSpeedKmh = boat.MaxSpeedKmh.Value * RouteSpeedFactor;
        }

        var hasViaWaterway = request.Waypoints.Any(waypoint =>
            string.Equals(waypoint.Type, WaypointTypes.ViaWaterway, StringComparison.OrdinalIgnoreCase));

        var hasChosenGeometry = request.ChosenGeometry is { Count: >= 2 };

        // geometryRequired: co via hoac autoRouteGeometry=true -> loi neu khong dung duoc geometry.
        // geometryBestEffort: chi nhap station (khong set co) -> co gang dung, that bai thi tao route khong geometry.
        // chosenGeometry (tu preview) co san -> khong can tu tinh.
        var geometryRequired = !hasChosenGeometry && (hasViaWaterway || request.AutoRouteGeometry == true);
        var geometryBestEffort = !hasChosenGeometry && !geometryRequired && request.AutoRouteGeometry != false;
        var buildGeometry = geometryRequired || geometryBestEffort;

        var waterwaySegments = buildGeometry
            ? await _context.Set<WaterwaySegment>()
                .AsNoTracking()
                .OrderBy(segment => segment.OsmId)
                .ThenBy(segment => segment.SegmentOrder)
                .ToListAsync(cancellationToken)
            : [];

        var avoidWaterwayIds = BuildAvoidWaterwaySet(request.AvoidWaterwayOsmIds);

        if (buildGeometry)
        {
            EnsureViaNotAvoided(request.Waypoints, avoidWaterwayIds);

            var filtered = string.IsNullOrWhiteSpace(request.PreferWaterwayType)
                ? waterwaySegments
                : waterwaySegments
                    .Where(s => string.Equals(s.WaterwayType, request.PreferWaterwayType.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                    .ToList();

            if (filtered.Count == 0)
            {
                if (geometryRequired)
                {
                    var detail = string.IsNullOrWhiteSpace(request.PreferWaterwayType)
                        ? "No waterway network has been imported yet. Import GeoJSON map data first or create the route with station waypoints only."
                        : $"No waterway segments of type '{request.PreferWaterwayType}' found. Import GeoJSON map data with the correct waterway type first.";
                    throw new ValidationException([new ValidationFailure(nameof(request.Waypoints), detail)]);
                }

                buildGeometry = false;
            }

            if (buildGeometry && avoidWaterwayIds.Count > 0)
            {
                filtered = filtered
                    .Where(s => !IsWaterwayAvoided(s, avoidWaterwayIds))
                    .ToList();

                if (filtered.Count == 0)
                {
                    if (geometryRequired)
                    {
                        throw new ValidationException([new ValidationFailure(
                            nameof(CreateRouteCommand.AvoidWaterwayOsmIds),
                            "avoidWaterwayOsmIds da loai bo toan bo mang waterway; khong con duong de tao route geometry.")]);
                    }

                    buildGeometry = false;
                }
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
                    try
                    {
                        waypointPoints.Add(GetStationPoint(station, nameof(request.Waypoints)));
                    }
                    catch (ValidationException) when (!geometryRequired)
                    {
                        // Best-effort: station thieu toa do -> bo qua geometry, van tao route.
                        buildGeometry = false;
                    }
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

        LineString? routeGeometry = null;
        if (hasChosenGeometry)
        {
            // Geometry admin da chon tu POST /api/routes/geometry-preview.
            routeGeometry = new LineString(
                request.ChosenGeometry!.Select(c => new Coordinate(c[0], c[1])).ToArray())
            { SRID = 4326 };
        }
        else if (buildGeometry)
        {
            try
            {
                routeGeometry = RouteGeoJsonImportSupport.BuildRouteGeometry(
                    waterwaySegments.Select(segment => segment.Geometry).ToList(),
                    waypointPoints);
            }
            catch (ValidationException) when (!geometryRequired)
            {
                // Best-effort: khong snap duoc / khong noi duoc -> van tao route, geometry rong.
                routeGeometry = null;
            }
        }
        var distanceKm = routeGeometry is null
            ? (decimal?)null
            : (decimal)Math.Round(RouteGeoJsonImportSupport.CalculateLengthKm(routeGeometry), 2);

        // EstimatedDurationMin = quang duong thuc te / van toc thuc te. Thieu 1 trong 2 (khong chon thuyen
        // hoac khong dung duoc geometry de co quang duong) -> de null.
        int? estimatedDurationMin = null;
        if (effectiveSpeedKmh is > 0 && distanceKm is > 0)
        {
            estimatedDurationMin = Math.Max(1, (int)Math.Round((double)distanceKm.Value / effectiveSpeedKmh.Value * 60));
        }

        var route = new Route
        {
            RouteCode = code,
            RouteName = request.RouteName.Trim(),
            Description = request.Description?.Trim(),
            BaseDistanceKm = distanceKm,
            EstimatedDurationMin = estimatedDurationMin,
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

    private static HashSet<string> BuildAvoidWaterwaySet(IReadOnlyList<string>? avoidWaterwayOsmIds) =>
        avoidWaterwayOsmIds is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : avoidWaterwayOsmIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsWaterwayAvoided(WaterwaySegment segment, HashSet<string> avoidWaterwayIds) =>
        (segment.OsmId is not null && avoidWaterwayIds.Contains(segment.OsmId))
        || (segment.WaterwayName is not null && avoidWaterwayIds.Contains(segment.WaterwayName));

    private static void EnsureViaNotAvoided(
        IReadOnlyList<CreateRouteWaypointDto> waypoints,
        HashSet<string> avoidWaterwayIds)
    {
        if (avoidWaterwayIds.Count == 0)
        {
            return;
        }

        foreach (var waypoint in waypoints)
        {
            if (string.Equals(waypoint.Type, WaypointTypes.ViaWaterway, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(waypoint.WaterwayOsmId)
                && avoidWaterwayIds.Contains(waypoint.WaterwayOsmId.Trim()))
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(CreateRouteCommand.AvoidWaterwayOsmIds),
                    $"Waterway '{waypoint.WaterwayOsmId.Trim()}' vua la viaWaterway (ep di qua) vua nam trong avoidWaterwayOsmIds (ep ne) - mau thuan.")]);
            }
        }
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
