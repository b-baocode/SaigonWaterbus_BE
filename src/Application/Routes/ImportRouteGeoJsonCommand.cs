using System.Globalization;
using System.Text;
using FluentValidation.Results;
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
    int RouteSegmentsCreated,
    int RouteSegmentsUpdated);

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
    private readonly IApplicationDbContext _context;

    public ImportRouteGeoJsonCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<GeoJsonImportResultDto> Handle(
        ImportRouteGeoJsonCommand request,
        CancellationToken cancellationToken)
    {
        var parsedGeoJson = RouteGeoJsonImportSupport.Parse(request.GeoJsonContent);

        if (parsedGeoJson.WaterwaySegments.Count == 0)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.GeoJsonContent),
                "GeoJSON must contain at least one LineString or MultiLineString feature.")]);
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

        await MergeWaterwaySegments(parsedGeoJson.WaterwaySegments, cancellationToken);
        var routeImportResult = await UpsertRoutesAndSegmentsAsync(
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
            routeImportResult.RouteSegmentsCreated,
            routeImportResult.RouteSegmentsUpdated);
    }

    private async Task<RouteImportResult> UpsertRoutesAndSegmentsAsync(
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
        var routeSegmentsCreated = 0;
        var routeSegmentsUpdated = 0;

        foreach (var candidate in routableSegments)
        {
            var fromStation = ResolveStationByCode(stations, candidate.FromStationCode!);
            var toStation = ResolveStationByCode(stations, candidate.ToStationCode!);
            if (fromStation is null || toStation is null)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(ImportRouteGeoJsonCommand.GeoJsonContent),
                    $"Feature #{candidate.FeatureIndex} co from_station_code/to_station_code nhung khong tim thay ben tuong ung.")]);
            }

            if (fromStation.Id == toStation.Id)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(ImportRouteGeoJsonCommand.GeoJsonContent),
                    $"Feature #{candidate.FeatureIndex} co from_station_code va to_station_code trung nhau.")]);
            }

            var routeCode = NormalizeRouteCode(
                candidate.RouteCode
                ?? candidate.Name
                ?? candidate.OsmId
                ?? $"{fromStation.StationCode}-{toStation.StationCode}");

            var route = await _context.Set<Route>()
                .Include(x => x.RouteStops)
                .Include(x => x.RouteSegments)
                .SingleOrDefaultAsync(x => x.RouteCode == routeCode, cancellationToken);

            var distanceKm = (decimal)Math.Round(RouteGeoJsonImportSupport.CalculateLengthKm(candidate.Geometry), 2);
            var estimatedTravelMinutes = CustomBookingRequests.CustomBookingRouteEstimator.EstimateTravelMinutes(distanceKm);

            if (route is null)
            {
                route = new Route
                {
                    RouteCode = routeCode,
                    RouteName = candidate.Name?.Trim() ?? routeCode,
                    Description = "Imported from GeoJSON",
                    BaseDistanceKm = distanceKm,
                    EstimatedDurationMin = estimatedTravelMinutes,
                    Status = "Active",
                    RouteGeometry = candidate.Geometry,
                    OsmId = candidate.OsmId
                };
                _context.Set<Route>().Add(route);
                routesCreated++;
            }
            else
            {
                route.RouteName = string.IsNullOrWhiteSpace(candidate.Name) ? route.RouteName : candidate.Name.Trim();
                route.BaseDistanceKm = distanceKm;
                route.EstimatedDurationMin = estimatedTravelMinutes;
                route.RouteGeometry = candidate.Geometry;
                route.OsmId = string.IsNullOrWhiteSpace(candidate.OsmId) ? route.OsmId : candidate.OsmId;
                routesUpdated++;
            }

            EnsureRouteStop(route, fromStation, isFirstStop: true);
            EnsureRouteStop(route, toStation, isFirstStop: false);

            var routeSegment = route.RouteSegments.FirstOrDefault(x =>
                x.FromStationId == fromStation.Id && x.ToStationId == toStation.Id);
            if (routeSegment is null)
            {
                routeSegment = new RouteSegment
                {
                    Route = route,
                    FromStationId = fromStation.Id,
                    ToStationId = toStation.Id,
                    SegmentOrder = NextSegmentOrder(route),
                    DistanceKm = distanceKm,
                    EstimatedTravelMinutes = estimatedTravelMinutes,
                    Geometry = candidate.Geometry
                };
                route.RouteSegments.Add(routeSegment);
                routeSegmentsCreated++;
            }
            else
            {
                routeSegment.DistanceKm = distanceKm;
                routeSegment.EstimatedTravelMinutes = estimatedTravelMinutes;
                routeSegment.Geometry = candidate.Geometry;
                routeSegmentsUpdated++;
            }
        }

        return new RouteImportResult(routesCreated, routesUpdated, routeSegmentsCreated, routeSegmentsUpdated);
    }

    private static Station? ResolveStationByCode(IReadOnlyList<Station> stations, string stationCode)
    {
        var code = stationCode.Trim().ToUpperInvariant();
        return stations.FirstOrDefault(x => x.StationCode == code)
            ?? (!code.StartsWith("ST-", StringComparison.OrdinalIgnoreCase)
                ? stations.FirstOrDefault(x => x.StationCode == $"ST-{code}")
                : null);
    }

    private static void EnsureRouteStop(Route route, Station station, bool isFirstStop)
    {
        if (route.RouteStops.Any(x => x.StationId == station.Id))
        {
            return;
        }

        var stopOrder = route.RouteStops.Count == 0
            ? 1
            : route.RouteStops.Max(x => x.StopOrder) + 1;
        route.RouteStops.Add(new RouteStop
        {
            Route = route,
            StationId = station.Id,
            StopOrder = stopOrder,
            IsPickupAllowed = isFirstStop,
            IsDropoffAllowed = !isFirstStop
        });
    }

    private static int NextSegmentOrder(Route route) =>
        route.RouteSegments.Count == 0
            ? 1
            : route.RouteSegments.Max(x => x.SegmentOrder) + 1;

    private static string NormalizeRouteCode(string value)
    {
        var normalized = RemoveDiacritics(value.Trim())
            .ToUpperInvariant()
            .Replace(" ", "-");
        return normalized.Length <= 50 ? normalized : normalized[..50];
    }

    private async Task MergeWaterwaySegments(
        IReadOnlyList<GeoJsonWaterwaySegmentCandidate> incoming,
        CancellationToken cancellationToken)
    {
        // Collect the two keys used for matching: OsmId and WaterwayName (for no-OsmId segments).
        var incomingOsmIds = incoming
            .Where(s => !string.IsNullOrWhiteSpace(s.OsmId))
            .Select(s => s.OsmId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var incomingNameOnlyWaterways = incoming
            .Where(s => string.IsNullOrWhiteSpace(s.OsmId) && !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove only segments that will be replaced by this import:
        //   • segments whose OsmId is in the incoming set
        //   • segments with no OsmId whose WaterwayName is covered by this import
        // Segments from other files (different OsmId / different name) are left untouched.
        var toRemove = await _context.Set<WaterwaySegment>()
            .Where(seg =>
                (seg.OsmId != null && incomingOsmIds.Contains(seg.OsmId)) ||
                (seg.OsmId == null && seg.WaterwayName != null && incomingNameOnlyWaterways.Contains(seg.WaterwayName)))
            .ToListAsync(cancellationToken);

        foreach (var seg in toRemove)
            _context.Set<WaterwaySegment>().Remove(seg);

        foreach (var candidate in incoming)
        {
            _context.Set<WaterwaySegment>().Add(new WaterwaySegment
            {
                OsmId = candidate.OsmId,
                WaterwayName = candidate.Name?.Trim(),
                WaterwayType = candidate.WaterwayType,
                SegmentOrder = candidate.SegmentOrder,
                LengthKm = (decimal)Math.Round(RouteGeoJsonImportSupport.CalculateLengthKm(candidate.Geometry), 2),
                Geometry = candidate.Geometry
            });
        }
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

            // Candidate has an OsmId but no DB match yet — the existing station may have been
            // imported from an older file that lacked @id. Upgrade it by matching on name,
            // but only if the DB station has no OsmId (to avoid stealing a station that already
            // belongs to a different OSM node).
            if (!string.IsNullOrWhiteSpace(candidate.Name))
            {
                var byNameNoOsmId = existingStations.FirstOrDefault(existing =>
                    string.IsNullOrWhiteSpace(existing.OsmId) &&
                    string.Equals(existing.StationName, candidate.Name, StringComparison.OrdinalIgnoreCase));
                if (byNameNoOsmId is not null)
                    return byNameNoOsmId;
            }
        }

        // Only fall back to name match when the candidate has no OsmId.
        // Two features with different OsmIds are distinct stations even if they share a name.
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

    // Words to skip when building the station abbreviation (matched after removing diacritics)
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
        var words = name.Split([' ', '-', '–', '\t'], StringSplitOptions.RemoveEmptyEntries);
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
        // "Đ/đ" has a stroke (not a combining mark) — NFD won't strip it; map explicitly
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

    private sealed record RouteImportResult(
        int RoutesCreated,
        int RoutesUpdated,
        int RouteSegmentsCreated,
        int RouteSegmentsUpdated);
}
