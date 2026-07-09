using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

/// <summary>
/// Xem truoc cac phuong an route geometry giua cac waypoint ma KHONG luu route.
/// Admin chon 1 phuong an roi goi POST /api/routes kem chosenGeometry.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record PreviewRouteGeometryCommand(
    IReadOnlyList<CreateRouteWaypointDto> Waypoints,
    string? PreferWaterwayType = null,
    IReadOnlyList<string>? AvoidWaterwayOsmIds = null,
    int MaxAlternatives = 3) : IRequest<IReadOnlyList<RouteGeometryAlternativeDto>>;

public sealed record RouteGeometryAlternativeDto(
    int Option,
    decimal DistanceKm,
    IReadOnlyList<double[]> Geometry);

public sealed class PreviewRouteGeometryCommandValidator : AbstractValidator<PreviewRouteGeometryCommand>
{
    public PreviewRouteGeometryCommandValidator()
    {
        RuleFor(x => x.MaxAlternatives).InclusiveBetween(1, 5);
        RuleFor(x => x.PreferWaterwayType)
            .Must(t => t == null || t == "river" || t == "canal" || t == "custom")
            .WithMessage("PreferWaterwayType phai la 'river', 'canal', hoac 'custom'.")
            .When(x => x.PreferWaterwayType != null);

        RuleFor(x => x.Waypoints)
            .NotNull()
            .Must(waypoints => waypoints is not null && waypoints.Count(IsStationWaypoint) >= 2)
            .WithMessage("Waypoints must contain at least 2 station waypoints.")
            .Must(waypoints => waypoints is not null
                && waypoints.Count >= 2
                && IsStationWaypoint(waypoints[0])
                && IsStationWaypoint(waypoints[^1]))
            .WithMessage("The first and last waypoint must be station waypoints.");

        RuleForEach(x => x.Waypoints)
            .SetValidator(new CreateRouteWaypointDtoValidator());
    }

    private static bool IsStationWaypoint(CreateRouteWaypointDto waypoint) =>
        string.Equals(waypoint.Type, WaypointTypes.Station, StringComparison.OrdinalIgnoreCase);
}

public sealed class PreviewRouteGeometryCommandHandler
    : IRequestHandler<PreviewRouteGeometryCommand, IReadOnlyList<RouteGeometryAlternativeDto>>
{
    private readonly IApplicationDbContext _context;

    public PreviewRouteGeometryCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<RouteGeometryAlternativeDto>> Handle(
        PreviewRouteGeometryCommand request,
        CancellationToken cancellationToken)
    {
        var waterwaySegments = await _context.Set<WaterwaySegment>()
            .AsNoTracking()
            .OrderBy(segment => segment.OsmId)
            .ThenBy(segment => segment.SegmentOrder)
            .ToListAsync(cancellationToken);

        waterwaySegments = RouteWaterwayFilterSupport.Filter(
            waterwaySegments,
            request.PreferWaterwayType,
            request.AvoidWaterwayOsmIds,
            request.Waypoints);

        var waypointPoints = await ResolveWaypointPointsAsync(request.Waypoints, waterwaySegments, cancellationToken);

        var alternatives = RouteGeoJsonImportSupport.BuildRouteGeometryAlternatives(
            waterwaySegments.Select(segment => segment.Geometry).ToList(),
            waypointPoints,
            request.MaxAlternatives);

        return alternatives
            .Select((geometry, index) => new RouteGeometryAlternativeDto(
                index + 1,
                (decimal)Math.Round(RouteGeoJsonImportSupport.CalculateLengthKm(geometry), 2),
                geometry.Coordinates.Select(c => new[] { c.X, c.Y }).ToArray()))
            .ToArray();
    }

    private async Task<IReadOnlyList<Point>> ResolveWaypointPointsAsync(
        IReadOnlyList<CreateRouteWaypointDto> waypoints,
        IReadOnlyList<WaterwaySegment> waterwaySegments,
        CancellationToken cancellationToken)
    {
        var stationCodes = waypoints
            .Where(w => string.Equals(w.Type, WaypointTypes.Station, StringComparison.OrdinalIgnoreCase))
            .Select(w => w.StationCode!.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var stationsByCode = await _context.Set<Station>()
            .AsNoTracking()
            .Where(station => stationCodes.Contains(station.StationCode))
            .ToDictionaryAsync(station => station.StationCode, cancellationToken);

        var points = new List<Point>();
        foreach (var waypoint in waypoints)
        {
            if (string.Equals(waypoint.Type, WaypointTypes.Station, StringComparison.OrdinalIgnoreCase))
            {
                var stationCode = waypoint.StationCode!.Trim().ToUpperInvariant();
                if (!stationsByCode.TryGetValue(stationCode, out var station))
                {
                    throw new ValidationException([new ValidationFailure(
                        nameof(PreviewRouteGeometryCommand.Waypoints),
                        $"Station '{stationCode}' was not found. Import the ferry terminal first or choose another station.")]);
                }

                if (!station.Latitude.HasValue || !station.Longitude.HasValue)
                {
                    throw new ValidationException([new ValidationFailure(
                        nameof(PreviewRouteGeometryCommand.Waypoints),
                        $"Station '{stationCode}' does not have a valid location.")]);
                }

                points.Add(new Point((double)station.Longitude.Value, (double)station.Latitude.Value) { SRID = 4326 });
                continue;
            }

            var waterwayRef = waypoint.WaterwayOsmId!.Trim();
            var viaGeometries = waterwaySegments
                .Where(s => string.Equals(s.OsmId, waterwayRef, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.WaterwayName, waterwayRef, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.SegmentOrder)
                .Select(s => s.Geometry)
                .ToList();

            if (viaGeometries.Count == 0)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(PreviewRouteGeometryCommand.Waypoints),
                    $"Waterway '{waterwayRef}' was not found by OSM ID or name. Import the latest GeoJSON map or choose another via.")]);
            }

            points.Add(RouteGeoJsonImportSupport.CreateRepresentativePoint(viaGeometries));
        }

        return points;
    }
}

/// <summary>Loc mang waterway theo preferWaterwayType + avoidWaterwayOsmIds (dung chung cho create/preview).</summary>
internal static class RouteWaterwayFilterSupport
{
    public static List<WaterwaySegment> Filter(
        List<WaterwaySegment> segments,
        string? preferWaterwayType,
        IReadOnlyList<string>? avoidWaterwayOsmIds,
        IReadOnlyList<CreateRouteWaypointDto> waypoints)
    {
        var avoid = avoidWaterwayOsmIds is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : avoidWaterwayOsmIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var waypoint in waypoints)
        {
            if (string.Equals(waypoint.Type, WaypointTypes.ViaWaterway, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(waypoint.WaterwayOsmId)
                && avoid.Contains(waypoint.WaterwayOsmId.Trim()))
            {
                throw new ValidationException([new ValidationFailure(
                    "AvoidWaterwayOsmIds",
                    $"Waterway '{waypoint.WaterwayOsmId.Trim()}' vua la viaWaterway (ep di qua) vua nam trong avoidWaterwayOsmIds (ep ne) - mau thuan.")]);
            }
        }

        var filtered = string.IsNullOrWhiteSpace(preferWaterwayType)
            ? segments
            : segments
                .Where(s => string.Equals(s.WaterwayType, preferWaterwayType.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                .ToList();

        if (avoid.Count > 0)
        {
            filtered = filtered
                .Where(s => !((s.OsmId is not null && avoid.Contains(s.OsmId))
                    || (s.WaterwayName is not null && avoid.Contains(s.WaterwayName))))
                .ToList();
        }

        if (filtered.Count == 0)
        {
            throw new ValidationException([new ValidationFailure(
                "Waypoints",
                "Khong con waterway nao sau khi loc (chua import mang, sai preferWaterwayType hoac avoid qua tay).")]);
        }

        return filtered;
    }
}
