using System.Text.Json;
using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

public sealed record ImportRouteGeoJsonCommand(
    string RouteCode,
    string RouteName,
    string GeoJsonContent) : IRequest<GeoJsonImportResultDto>;

public sealed record GeoJsonImportResultDto(
    Guid RouteId,
    string RouteCode,
    string RouteName,
    decimal BaseDistanceKm,
    int StationsCreated,
    int StationsUpdated,
    int RouteStopsCreated);

public sealed class ImportRouteGeoJsonCommandValidator : AbstractValidator<ImportRouteGeoJsonCommand>
{
    public ImportRouteGeoJsonCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.RouteName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.GeoJsonContent).NotEmpty();
    }
}

public sealed class ImportRouteGeoJsonCommandHandler : IRequestHandler<ImportRouteGeoJsonCommand, GeoJsonImportResultDto>
{
    private const double ProximityThresholdMeters = 100;
    private readonly IApplicationDbContext _context;

    public ImportRouteGeoJsonCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<GeoJsonImportResultDto> Handle(
        ImportRouteGeoJsonCommand request, CancellationToken cancellationToken)
    {
        var (routeGeometry, routeOsmId, stationCandidates) = ParseGeoJson(request.GeoJsonContent);

        if (routeGeometry is null)
            throw new ValidationException([new ValidationFailure(
                nameof(request.GeoJsonContent),
                "GeoJSON must contain at least one LineString feature.")]);

        if (stationCandidates.Count == 0)
            throw new ValidationException([new ValidationFailure(
                nameof(request.GeoJsonContent),
                "GeoJSON must contain at least one ferry_terminal Point feature.")]);

        var distanceKm = (decimal)Math.Round(CalculateLengthKm(routeGeometry), 2);

        // 1. Find or create Route
        var routeCode = request.RouteCode.Trim().ToUpperInvariant();
        var route = await _context.Set<Route>()
            .FirstOrDefaultAsync(r => r.RouteCode == routeCode, cancellationToken);

        if (route is null)
        {
            route = new Route
            {
                RouteCode = routeCode,
                RouteName = request.RouteName.Trim(),
                Status = "Draft"
            };
            _context.Set<Route>().Add(route);
        }
        else
        {
            route.RouteName = request.RouteName.Trim();
        }

        route.RouteGeometry = routeGeometry;
        route.BaseDistanceKm = distanceKm;
        if (!string.IsNullOrEmpty(routeOsmId))
            route.OsmId = routeOsmId;

        // 2. Match or create stations
        var existingStations = await _context.Set<Station>().ToListAsync(cancellationToken);
        int stationsCreated = 0, stationsUpdated = 0;
        var positioned = new List<(Station Station, double Fraction)>();

        foreach (var candidate in stationCandidates)
        {
            var candidatePoint = new Point(candidate.Longitude, candidate.Latitude) { SRID = 4326 };
            Station? station = null;

            // Priority 1: OsmId exact match
            if (!string.IsNullOrEmpty(candidate.OsmId))
                station = existingStations.FirstOrDefault(s => s.OsmId == candidate.OsmId);

            // Priority 2: station name exact match (case-insensitive)
            if (station is null && !string.IsNullOrEmpty(candidate.Name))
                station = existingStations.FirstOrDefault(s =>
                    string.Equals(s.StationName, candidate.Name, StringComparison.OrdinalIgnoreCase));

            // Priority 3: proximity < 100 m
            if (station is null)
                station = existingStations
                    .Where(s => s.Latitude.HasValue && s.Longitude.HasValue)
                    .Select(s => new
                    {
                        Station = s,
                        Dist = HaversineMeters(
                            candidate.Latitude, candidate.Longitude,
                            (double)s.Latitude!, (double)s.Longitude!)
                    })
                    .Where(x => x.Dist < ProximityThresholdMeters)
                    .OrderBy(x => x.Dist)
                    .FirstOrDefault()?.Station;

            if (station is null)
            {
                var code = MakeStationCode(candidate.OsmId, candidate.Name, existingStations);
                station = new Station
                {
                    StationCode = code,
                    StationName = candidate.Name ?? code,
                    Status = "Active"
                };
                _context.Set<Station>().Add(station);
                existingStations.Add(station);
                stationsCreated++;
            }
            else
            {
                stationsUpdated++;
            }

            station.Location = candidatePoint;
            station.Latitude = (decimal)candidate.Latitude;
            station.Longitude = (decimal)candidate.Longitude;
            if (!string.IsNullOrEmpty(candidate.OsmId))
                station.OsmId = candidate.OsmId;

            positioned.Add((station, FractionAlongLine(routeGeometry, candidatePoint)));
        }

        // 3. Persist route + stations to obtain stable IDs before creating RouteStops
        await _context.SaveChangesAsync(cancellationToken);

        // 4. Replace RouteStops (full re-import)
        var existing = await _context.Set<RouteStop>()
            .Where(rs => rs.RouteId == route.Id)
            .ToListAsync(cancellationToken);

        foreach (var stop in existing)
            _context.Set<RouteStop>().Remove(stop);

        var sorted = positioned.OrderBy(p => p.Fraction).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var order = i + 1;
            _context.Set<RouteStop>().Add(new RouteStop
            {
                RouteId = route.Id,
                StationId = sorted[i].Station.Id,
                StopOrder = order,
                IsPickupAllowed = order < sorted.Count,
                IsDropoffAllowed = order > 1
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new GeoJsonImportResultDto(
            route.Id, route.RouteCode, route.RouteName,
            route.BaseDistanceKm ?? 0,
            stationsCreated, stationsUpdated,
            sorted.Count);
    }

    // ─────────────────────────── GeoJSON parsing ──────────────────────────────

    private sealed record StationCandidate(
        string? OsmId, string? Name, double Latitude, double Longitude);

    private static (LineString? Geometry, string? RouteOsmId, List<StationCandidate> Stations)
        ParseGeoJson(string geoJson)
    {
        LineString? geometry = null;
        string? routeOsmId = null;
        var stations = new List<StationCandidate>();

        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                var geom = feature.GetProperty("geometry");
                var type = geom.GetProperty("type").GetString();
                var osmId = GetPropString(feature, "@id");

                if (type == "LineString" && geometry is null)
                {
                    var coords = geom.GetProperty("coordinates").EnumerateArray()
                        .Select(c => new Coordinate(c[0].GetDouble(), c[1].GetDouble()))
                        .ToArray();
                    if (coords.Length >= 2)
                    {
                        geometry = new LineString(coords) { SRID = 4326 };
                        routeOsmId = osmId;
                    }
                }
                else if (type == "Point")
                {
                    var c = geom.GetProperty("coordinates");
                    stations.Add(new StationCandidate(
                        OsmId: osmId,
                        Name: GetPropString(feature, "name"),
                        Latitude: c[1].GetDouble(),
                        Longitude: c[0].GetDouble()));
                }
            }
        }
        catch (JsonException ex)
        {
            throw new ValidationException([
                new ValidationFailure("GeoJsonContent", $"Invalid GeoJSON: {ex.Message}")]);
        }

        return (geometry, routeOsmId, stations);
    }

    private static string? GetPropString(JsonElement feature, string key)
    {
        if (!feature.TryGetProperty("properties", out var props)) return null;
        if (!props.TryGetProperty(key, out var val)) return null;
        return val.ValueKind == JsonValueKind.String ? val.GetString() : null;
    }

    // ─────────────────────────── Geometry helpers ─────────────────────────────

    // Returns normalised arc-length fraction [0,1] of point's projection onto line.
    private static double FractionAlongLine(LineString line, Point point)
    {
        double totalLength = 0;
        double lengthAtClosest = 0;
        double minDist = double.MaxValue;

        for (var i = 0; i < line.NumPoints - 1; i++)
        {
            var ax = line.GetPointN(i).X; var ay = line.GetPointN(i).Y;
            var bx = line.GetPointN(i + 1).X; var by = line.GetPointN(i + 1).Y;
            var segLen = HaversineMeters(ay, ax, by, bx);

            var dx = bx - ax; var dy = by - ay;
            var lenSq = dx * dx + dy * dy;
            var t = lenSq > 0
                ? Math.Clamp(((point.X - ax) * dx + (point.Y - ay) * dy) / lenSq, 0.0, 1.0)
                : 0.0;

            var dist = HaversineMeters(point.Y, point.X, ay + t * dy, ax + t * dx);
            if (dist < minDist)
            {
                minDist = dist;
                lengthAtClosest = totalLength + t * segLen;
            }

            totalLength += segLen;
        }

        return totalLength > 0 ? lengthAtClosest / totalLength : 0;
    }

    private static double CalculateLengthKm(LineString line)
    {
        double total = 0;
        for (var i = 0; i < line.NumPoints - 1; i++)
            total += HaversineMeters(
                line.GetPointN(i).Y, line.GetPointN(i).X,
                line.GetPointN(i + 1).Y, line.GetPointN(i + 1).X);
        return total / 1000.0;
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static string MakeStationCode(string? osmId, string? name, List<Station> existing)
    {
        string candidate;
        if (osmId is not null)
        {
            candidate = ("ST-" + osmId.Split('/').Last()).ToUpperInvariant();
        }
        else if (name is not null)
        {
            var safe = new string(name.Where(char.IsAsciiLetterOrDigit).ToArray());
            candidate = ("ST-" + safe[..Math.Min(8, safe.Length)]).ToUpperInvariant();
        }
        else
        {
            candidate = ("ST-" + Guid.NewGuid().ToString("N")[..8]).ToUpperInvariant();
        }

        candidate = candidate[..Math.Min(50, candidate.Length)];

        if (existing.Any(s => s.StationCode == candidate))
            candidate = ("ST-" + Guid.NewGuid().ToString("N")[..8]).ToUpperInvariant();

        return candidate;
    }
}
