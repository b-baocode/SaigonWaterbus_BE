using System.Globalization;
using System.Text;
using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

public sealed record ImportRouteGeoJsonCommand(
    string GeoJsonContent) : IRequest<GeoJsonImportResultDto>;

public sealed record GeoJsonImportResultDto(
    int WaterwaySegmentsImported,
    int StationsCreated,
    int StationsUpdated,
    int RoutesCreated,
    int RoutesUpdated,
    int RouteStopsCreated,
    int RouteStopsUpdated);

public sealed class ImportRouteGeoJsonCommandValidator : AbstractValidator<ImportRouteGeoJsonCommand>
{
    public ImportRouteGeoJsonCommandValidator()
    {
        RuleFor(x => x.GeoJsonContent).NotEmpty();
    }
}

public sealed class ImportRouteGeoJsonCommandHandler : IRequestHandler<ImportRouteGeoJsonCommand, GeoJsonImportResultDto>
{
    private const double ProximityThresholdMeters = 100;
    private const int DefaultRouteSpeedKmh = 15;
    private readonly IApplicationDbContext _context;

    public ImportRouteGeoJsonCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<GeoJsonImportResultDto> Handle(
        ImportRouteGeoJsonCommand request,
        CancellationToken cancellationToken)
    {
        var parsedGeoJson = RouteGeoJsonImportSupport.Parse(request.GeoJsonContent);

        if (parsedGeoJson.WaterwaySegments.Count == 0 && parsedGeoJson.StationCandidates.Count == 0)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.GeoJsonContent),
                "GeoJSON must contain at least one LineString/MultiLineString or one Point with amenity=ferry_terminal.")]);
        }

        var existingStations = await _context.Set<Station>().ToListAsync(cancellationToken);
        var stationsCreated = 0;
        var stationsUpdated = 0;

        foreach (var candidate in parsedGeoJson.StationCandidates)
        {
            var station = MatchExistingStation(existingStations, candidate);
            if (station is null)
            {
                station = new Station
                {
                    StationCode = MakeStationCode(candidate.OsmId, candidate.Name, existingStations),
                    StationName = candidate.Name?.Trim() ?? "Unnamed ferry terminal",
                    Status = StationStatus.Active
                };

                _context.Set<Station>().Add(station);
                existingStations.Add(station);
                stationsCreated++;
            }
            else
            {
                stationsUpdated++;
            }

            station.StationName = candidate.Name?.Trim() ?? station.StationName;
            station.Location = candidate.ToPoint();
            station.Latitude = (decimal)candidate.Latitude;
            station.Longitude = (decimal)candidate.Longitude;
            if (!string.IsNullOrWhiteSpace(candidate.OsmId))
            {
                station.OsmId = candidate.OsmId;
            }
        }

        var routeImportResult = await UpsertRoutesAsync(
            parsedGeoJson.WaterwaySegments,
            existingStations,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return new GeoJsonImportResultDto(
            parsedGeoJson.WaterwaySegments.Count,
            stationsCreated,
            stationsUpdated,
            routeImportResult.RoutesCreated,
            routeImportResult.RoutesUpdated,
            routeImportResult.RouteStopsCreated,
            routeImportResult.RouteStopsUpdated);
    }

    private async Task<RouteImportResult> UpsertRoutesAsync(
        IReadOnlyList<GeoJsonWaterwaySegmentCandidate> incoming,
        IReadOnlyList<Station> stations,
        CancellationToken cancellationToken)
    {
        var routableSegments = incoming
            .Where(x => !string.IsNullOrWhiteSpace(x.FromStationCode)
                        && !string.IsNullOrWhiteSpace(x.ToStationCode))
            .ToArray();

        if (routableSegments.Length == 0)
        {
            return new RouteImportResult(0, 0, 0, 0);
        }

        var routesCreated = 0;
        var routesUpdated = 0;
        var routeStopsCreated = 0;
        var routeStopsUpdated = 0;

        var routeGroups = routableSegments
            .GroupBy(candidate => ResolveRouteCode(candidate, stations), StringComparer.OrdinalIgnoreCase);

        foreach (var routeGroup in routeGroups)
        {
            var resolvedSegments = ResolveRouteSegments(routeGroup, stations);
            var orderedStations = BuildOrderedStations(resolvedSegments);
            if (orderedStations.Count < 2)
            {
                continue;
            }

            var route = await _context.Set<Route>()
                .Include(x => x.RouteStops)
                .SingleOrDefaultAsync(x => x.RouteCode == routeGroup.Key, cancellationToken);

            var distanceKm = (decimal)Math.Round(
                resolvedSegments.Sum(x => RouteGeoJsonImportSupport.CalculateLengthKm(x.Candidate.Geometry)),
                2);
            var estimatedDurationMin = ResolveEstimatedDurationMinutes(resolvedSegments, distanceKm);
            var routeGeometry = BuildRouteGeometry(resolvedSegments);
            var firstSegment = resolvedSegments[0].Candidate;

            if (route is null)
            {
                route = new Route
                {
                    RouteCode = routeGroup.Key,
                    RouteName = firstSegment.Name?.Trim() ?? routeGroup.Key,
                    Description = "Imported from GeoJSON",
                    Status = "Active"
                };
                _context.Set<Route>().Add(route);
                routesCreated++;
            }
            else
            {
                route.RouteName = string.IsNullOrWhiteSpace(firstSegment.Name)
                    ? route.RouteName
                    : firstSegment.Name.Trim();
                routesUpdated++;
            }

            route.BaseDistanceKm = distanceKm;
            route.EstimatedDurationMin = estimatedDurationMin;
            route.RouteGeometry = routeGeometry;
            if (!string.IsNullOrWhiteSpace(firstSegment.OsmId))
            {
                route.OsmId = firstSegment.OsmId;
            }

            var existingStops = route.RouteStops.ToList();
            routeStopsUpdated += existingStops.Count;
            foreach (var stop in existingStops)
            {
                _context.Set<RouteStop>().Remove(stop);
            }

            route.RouteStops.Clear();
            for (var i = 0; i < orderedStations.Count; i++)
            {
                _context.Set<RouteStop>().Add(new RouteStop
                {
                    RouteId = route.Id,
                    StationId = orderedStations[i].Id,
                    StopOrder = i + 1,
                    IsPickupAllowed = i < orderedStations.Count - 1,
                    IsDropoffAllowed = i > 0
                });
                routeStopsCreated++;
            }
        }

        return new RouteImportResult(routesCreated, routesUpdated, routeStopsCreated, routeStopsUpdated);
    }

    private static IReadOnlyList<ResolvedRouteSegment> ResolveRouteSegments(
        IEnumerable<GeoJsonWaterwaySegmentCandidate> candidates,
        IReadOnlyList<Station> stations) =>
        candidates
            .OrderBy(x => x.FeatureIndex)
            .ThenBy(x => x.SegmentOrder)
            .Select(candidate =>
            {
                var fromStation = ResolveStationByCode(stations, candidate.FromStationCode!);
                var toStation = ResolveStationByCode(stations, candidate.ToStationCode!);
                if (fromStation is null || toStation is null)
                {
                    throw new ValidationException([new ValidationFailure(
                        nameof(ImportRouteGeoJsonCommand.GeoJsonContent),
                        $"Feature #{candidate.FeatureIndex} has from_station_code/to_station_code but station was not found.")]);
                }

                if (fromStation.Id == toStation.Id)
                {
                    throw new ValidationException([new ValidationFailure(
                        nameof(ImportRouteGeoJsonCommand.GeoJsonContent),
                        $"Feature #{candidate.FeatureIndex} has identical from_station_code and to_station_code.")]);
                }

                return new ResolvedRouteSegment(candidate, fromStation, toStation);
            })
            .ToArray();

    private static List<Station> BuildOrderedStations(IReadOnlyList<ResolvedRouteSegment> segments)
    {
        var orderedStations = new List<Station>();
        foreach (var segment in segments)
        {
            AddStationIfNeeded(orderedStations, segment.FromStation);
            AddStationIfNeeded(orderedStations, segment.ToStation);
        }

        return orderedStations;
    }

    private static void AddStationIfNeeded(List<Station> stations, Station station)
    {
        if (stations.Count == 0 || stations[^1].Id != station.Id)
        {
            stations.Add(station);
        }
    }

    private static string ResolveRouteCode(
        GeoJsonWaterwaySegmentCandidate candidate,
        IReadOnlyList<Station> stations)
    {
        var fromStation = ResolveStationByCode(stations, candidate.FromStationCode!);
        var toStation = ResolveStationByCode(stations, candidate.ToStationCode!);

        return NormalizeRouteCode(
            candidate.RouteCode
            ?? candidate.Name
            ?? candidate.OsmId
            ?? $"{fromStation?.StationCode ?? candidate.FromStationCode}-{toStation?.StationCode ?? candidate.ToStationCode}");
    }

    private static Station? ResolveStationByCode(IReadOnlyList<Station> stations, string stationCode)
    {
        var code = stationCode.Trim().ToUpperInvariant();
        return stations.FirstOrDefault(x => x.StationCode == code)
            ?? (!code.StartsWith("ST-", StringComparison.OrdinalIgnoreCase)
                ? stations.FirstOrDefault(x => x.StationCode == $"ST-{code}")
                : null);
    }

    private static int ResolveEstimatedDurationMinutes(
        IReadOnlyList<ResolvedRouteSegment> segments,
        decimal distanceKm)
    {
        var explicitDurations = segments
            .Select(x => x.Candidate.EstimatedTravelMinutes)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToArray();

        if (explicitDurations.Length > 0)
        {
            return explicitDurations.Sum();
        }

        return Math.Max(1, (int)Math.Round((double)distanceKm / DefaultRouteSpeedKmh * 60));
    }

    private static LineString BuildRouteGeometry(IReadOnlyList<ResolvedRouteSegment> segments)
    {
        var coordinates = new List<Coordinate>();
        foreach (var segment in segments)
        {
            var oriented = OrientCoordinates(
                segment.Candidate.Geometry.Coordinates,
                segment.FromStation,
                segment.ToStation);
            AppendCoordinates(coordinates, oriented);
        }

        if (coordinates.Count < 2)
        {
            return segments[0].Candidate.Geometry;
        }

        return new LineString(coordinates.ToArray()) { SRID = 4326 };
    }

    private static Coordinate[] OrientCoordinates(
        Coordinate[] coordinates,
        Station fromStation,
        Station toStation)
    {
        if (coordinates.Length < 2
            || !fromStation.Latitude.HasValue
            || !fromStation.Longitude.HasValue
            || !toStation.Latitude.HasValue
            || !toStation.Longitude.HasValue)
        {
            return coordinates;
        }

        var start = coordinates[0];
        var end = coordinates[^1];
        var normalDistance =
            DistanceToStationMeters(start, fromStation) + DistanceToStationMeters(end, toStation);
        var reverseDistance =
            DistanceToStationMeters(start, toStation) + DistanceToStationMeters(end, fromStation);

        return reverseDistance < normalDistance
            ? coordinates.Reverse().ToArray()
            : coordinates;
    }

    private static double DistanceToStationMeters(Coordinate coordinate, Station station) =>
        RouteGeoJsonImportSupport.HaversineMeters(
            coordinate.Y,
            coordinate.X,
            (double)station.Latitude!.Value,
            (double)station.Longitude!.Value);

    private static void AppendCoordinates(List<Coordinate> destination, IReadOnlyList<Coordinate> coordinates)
    {
        foreach (var coordinate in coordinates)
        {
            if (destination.Count == 0 || !CoordinatesEqual(destination[^1], coordinate))
            {
                destination.Add(new Coordinate(coordinate.X, coordinate.Y));
            }
        }
    }

    private static bool CoordinatesEqual(Coordinate a, Coordinate b) =>
        RouteGeoJsonImportSupport.HaversineMeters(a.Y, a.X, b.Y, b.X) <= 1;

    private static string NormalizeRouteCode(string value)
    {
        var normalized = RemoveDiacritics(value.Trim())
            .ToUpperInvariant()
            .Replace(" ", "-");
        return normalized.Length <= 50 ? normalized : normalized[..50];
    }

    private static Station? MatchExistingStation(
        IReadOnlyList<Station> existingStations,
        GeoJsonStationCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.OsmId))
        {
            var byOsmId = existingStations.FirstOrDefault(existing => existing.OsmId == candidate.OsmId);
            if (byOsmId is not null)
                return byOsmId;

            if (!string.IsNullOrWhiteSpace(candidate.Name))
            {
                var byNameNoOsmId = existingStations.FirstOrDefault(existing =>
                    string.IsNullOrWhiteSpace(existing.OsmId) &&
                    string.Equals(existing.StationName, candidate.Name, StringComparison.OrdinalIgnoreCase));
                if (byNameNoOsmId is not null)
                    return byNameNoOsmId;
            }
        }

        if (string.IsNullOrWhiteSpace(candidate.OsmId) && !string.IsNullOrWhiteSpace(candidate.Name))
        {
            var byName = existingStations.FirstOrDefault(existing =>
                string.Equals(existing.StationName, candidate.Name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
                return byName;
        }

        return existingStations
            .Where(existing => existing.Latitude.HasValue && existing.Longitude.HasValue)
            .Select(existing => new
            {
                Station = existing,
                Distance = RouteGeoJsonImportSupport.HaversineMeters(
                    candidate.Latitude,
                    candidate.Longitude,
                    (double)existing.Latitude!,
                    (double)existing.Longitude!)
            })
            .Where(match => match.Distance < ProximityThresholdMeters)
            .OrderBy(match => match.Distance)
            .FirstOrDefault()
            ?.Station;
    }

    private static readonly HashSet<string> AbbrevSkipWords =
        new(["ben", "cang", "do"], StringComparer.OrdinalIgnoreCase);

    private static string MakeStationCode(string? osmId, string? name, IReadOnlyList<Station> existingStations)
    {
        string baseCode;

        var isUnnamed = string.IsNullOrWhiteSpace(name)
            || name.StartsWith("Unnamed", StringComparison.OrdinalIgnoreCase);

        if (!isUnnamed)
        {
            baseCode = BuildAbbreviationCode(name!);
        }
        else if (!string.IsNullOrWhiteSpace(osmId))
        {
            var osmPart = osmId.Split('/').Last();
            baseCode = ("ST-" + osmPart)[..Math.Min(50, 3 + osmPart.Length)].ToUpperInvariant();
        }
        else
        {
            baseCode = ("ST-" + Guid.NewGuid().ToString("N")[..8]).ToUpperInvariant();
        }

        if (!existingStations.Any(s => s.StationCode == baseCode))
        {
            return baseCode;
        }

        for (var suffix = 2; ; suffix++)
        {
            var withSuffix = baseCode + suffix;
            if (withSuffix.Length > 50)
            {
                return ("ST-" + Guid.NewGuid().ToString("N")[..8]).ToUpperInvariant();
            }

            if (!existingStations.Any(s => s.StationCode == withSuffix))
            {
                return withSuffix;
            }
        }
    }

    private static string BuildAbbreviationCode(string name)
    {
        var words = name.Split([' ', '-', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var initials = words
            .Where(w => w.Length > 0 && char.IsLetter(w[0]))
            .Where(w => !AbbrevSkipWords.Contains(RemoveDiacritics(w)))
            .Select(w => RemoveDiacritics(w[0].ToString()).ToUpperInvariant())
            .ToArray();

        return initials.Length > 0
            ? "ST-" + string.Join("", initials)
            : ("ST-" + Guid.NewGuid().ToString("N")[..8]).ToUpperInvariant();
    }

    private static string RemoveDiacritics(string text)
    {
        var replaced = text.Replace('Đ', 'D').Replace('đ', 'd');
        var normalized = replaced.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record ResolvedRouteSegment(
        GeoJsonWaterwaySegmentCandidate Candidate,
        Station FromStation,
        Station ToStation);

    private sealed record RouteImportResult(
        int RoutesCreated,
        int RoutesUpdated,
        int RouteStopsCreated,
        int RouteStopsUpdated);
}
