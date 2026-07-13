using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Routes;

public sealed record UpdateRouteGeoJsonCommand(
    Guid RouteId,
    string GeoJsonContent,
    bool UpdateStations = true) : IRequest<RouteGeoJsonUpdateResultDto>;

public sealed record RouteGeoJsonUpdateResultDto(
    Guid RouteId,
    int RouteLineFeaturesUsed,
    int StationsUpdated,
    decimal BaseDistanceKm,
    IReadOnlyList<double[]> RouteGeometry,
    RouteDetailDto Route);

public sealed class UpdateRouteGeoJsonCommandValidator : AbstractValidator<UpdateRouteGeoJsonCommand>
{
    public UpdateRouteGeoJsonCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.GeoJsonContent).NotEmpty();
    }
}

public sealed class UpdateRouteGeoJsonCommandHandler
    : IRequestHandler<UpdateRouteGeoJsonCommand, RouteGeoJsonUpdateResultDto>
{
    private const double StationMatchThresholdMeters = 100;
    private readonly IApplicationDbContext _context;

    public UpdateRouteGeoJsonCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<RouteGeoJsonUpdateResultDto> Handle(
        UpdateRouteGeoJsonCommand request,
        CancellationToken cancellationToken)
    {
        var route = await _context.Set<Route>()
            .Include(x => x.RouteStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == request.RouteId, cancellationToken)
            ?? throw new NotFoundException("Route not found.");

        var parsedGeoJson = RouteGeoJsonImportSupport.Parse(request.GeoJsonContent);
        if (parsedGeoJson.WaterwaySegments.Count == 0)
        {
            throw CreateValidationException(
                nameof(request.GeoJsonContent),
                "GeoJSON phai co it nhat mot LineString hoac MultiLineString de cap nhat route geometry.");
        }

        var stationsUpdated = request.UpdateStations
            ? UpdateRouteStations(route, parsedGeoJson.StationCandidates)
            : 0;

        var routeCodeMatchedSegments = parsedGeoJson.WaterwaySegments
            .Where(segment => RouteCodesEqual(segment.RouteCode, route.RouteCode))
            .OrderBy(segment => segment.FeatureIndex)
            .ThenBy(segment => segment.SegmentOrder)
            .ToArray();

        var routeSegments = routeCodeMatchedSegments.Length > 0
            ? routeCodeMatchedSegments
            : parsedGeoJson.WaterwaySegments
                .OrderBy(segment => segment.FeatureIndex)
                .ThenBy(segment => segment.SegmentOrder)
                .ToArray();

        var routeGeometry = BuildRouteGeometry(
            route,
            routeSegments,
            routeCodeMatchedSegments.Length > 0,
            nameof(request.GeoJsonContent));

        route.RouteGeometry = routeGeometry;
        route.BaseDistanceKm = (decimal)Math.Round(RouteGeoJsonImportSupport.CalculateLengthKm(routeGeometry), 2);

        await _context.SaveChangesAsync(cancellationToken);

        var geometry = ToCoordinateDtos(routeGeometry);
        return new RouteGeoJsonUpdateResultDto(
            route.Id,
            routeSegments.Length,
            stationsUpdated,
            route.BaseDistanceKm ?? 0,
            geometry,
            ToRouteDetailDto(route, geometry));
    }

    private static int UpdateRouteStations(
        Route route,
        IReadOnlyList<GeoJsonStationCandidate> stationCandidates)
    {
        if (stationCandidates.Count == 0)
        {
            return 0;
        }

        var stops = route.RouteStops
            .OrderBy(x => x.StopOrder)
            .ToArray();

        var updatedStationIds = new HashSet<Guid>();
        foreach (var candidate in stationCandidates)
        {
            var station = MatchRouteStation(stops, candidate);
            if (station is null || updatedStationIds.Contains(station.Id))
            {
                continue;
            }

            if (UpdateStationCoordinates(station, candidate.Latitude, candidate.Longitude))
            {
                updatedStationIds.Add(station.Id);
            }
        }

        return updatedStationIds.Count;
    }

    private static Station? MatchRouteStation(
        IReadOnlyList<RouteStop> stops,
        GeoJsonStationCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.StationCode))
        {
            var stationCode = NormalizeCode(candidate.StationCode);
            var station = stops
                .Select(x => x.Station)
                .FirstOrDefault(x =>
                    string.Equals(x.StationCode, stationCode, StringComparison.OrdinalIgnoreCase)
                    || (!stationCode.StartsWith("ST-", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.StationCode, $"ST-{stationCode}", StringComparison.OrdinalIgnoreCase)));

            if (station is not null)
            {
                return station;
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.Name))
        {
            var station = stops
                .Select(x => x.Station)
                .FirstOrDefault(x => string.Equals(
                    x.StationName,
                    candidate.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (station is not null)
            {
                return station;
            }
        }

        return stops
            .Select(x => x.Station)
            .Where(x => x.Latitude.HasValue && x.Longitude.HasValue)
            .Select(x => new
            {
                Station = x,
                Distance = RouteGeoJsonImportSupport.HaversineMeters(
                    candidate.Latitude,
                    candidate.Longitude,
                    (double)x.Latitude!.Value,
                    (double)x.Longitude!.Value)
            })
            .Where(x => x.Distance <= StationMatchThresholdMeters)
            .OrderBy(x => x.Distance)
            .FirstOrDefault()
            ?.Station;
    }

    private static bool UpdateStationCoordinates(Station station, double latitude, double longitude)
    {
        var newLatitude = Math.Round((decimal)latitude, 7);
        var newLongitude = Math.Round((decimal)longitude, 7);

        if (station.Latitude == newLatitude && station.Longitude == newLongitude)
        {
            return false;
        }

        station.Latitude = newLatitude;
        station.Longitude = newLongitude;
        return true;
    }

    private static LineString BuildRouteGeometry(
        Route route,
        IReadOnlyList<GeoJsonWaterwaySegmentCandidate> routeSegments,
        bool routeCodeMatched,
        string validationField)
    {
        if (routeSegments.Count == 1)
        {
            return routeSegments[0].Geometry;
        }

        var waypointPoints = CreateRouteStopWaypointPoints(route);
        if (waypointPoints.Count >= 2)
        {
            try
            {
                return RouteGeoJsonImportSupport.BuildRouteGeometry(
                    routeSegments.Select(x => x.Geometry).ToArray(),
                    waypointPoints);
            }
            catch (ValidationException) when (routeCodeMatched)
            {
                return MergeRouteLineStrings(routeSegments, validationField);
            }
        }

        if (routeCodeMatched)
        {
            return MergeRouteLineStrings(routeSegments, validationField);
        }

        throw CreateValidationException(
            validationField,
            "GeoJSON co nhieu LineString nhung khong co route_code/waterbus_route khop route va route chua du toa do station de tao duong tu mang waterway.");
    }

    private static IReadOnlyList<Point> CreateRouteStopWaypointPoints(Route route) =>
        route.RouteStops
            .OrderBy(x => x.StopOrder)
            .Select(x => ToStationPoint(x.Station))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

    private static Point? ToStationPoint(Station station)
    {
        if (!station.Latitude.HasValue || !station.Longitude.HasValue)
        {
            return null;
        }

        return new Point((double)station.Longitude.Value, (double)station.Latitude.Value) { SRID = 4326 };
    }

    private static LineString MergeRouteLineStrings(
        IReadOnlyList<GeoJsonWaterwaySegmentCandidate> routeSegments,
        string validationField)
    {
        var coordinates = new List<Coordinate>();
        foreach (var segment in routeSegments)
        {
            var nextCoordinates = segment.Geometry.Coordinates;
            if (coordinates.Count > 0 && nextCoordinates.Length > 1)
            {
                var lastCoordinate = coordinates[^1];
                var forwardDistance = DistanceMeters(lastCoordinate, nextCoordinates[0]);
                var reverseDistance = DistanceMeters(lastCoordinate, nextCoordinates[^1]);
                if (reverseDistance < forwardDistance)
                {
                    nextCoordinates = nextCoordinates.Reverse().ToArray();
                }
            }

            AppendCoordinates(coordinates, nextCoordinates);
        }

        if (coordinates.Count < 2)
        {
            throw CreateValidationException(
                validationField,
                "Khong the tao LineString hop le tu GeoJSON da upload.");
        }

        return new LineString(coordinates.ToArray()) { SRID = 4326 };
    }

    private static void AppendCoordinates(List<Coordinate> destination, IReadOnlyList<Coordinate> coordinates)
    {
        foreach (var coordinate in coordinates)
        {
            if (destination.Count == 0 || DistanceMeters(destination[^1], coordinate) > 1)
            {
                destination.Add(new Coordinate(coordinate.X, coordinate.Y));
            }
        }
    }

    private static double DistanceMeters(Coordinate a, Coordinate b) =>
        RouteGeoJsonImportSupport.HaversineMeters(a.Y, a.X, b.Y, b.X);

    private static IReadOnlyList<double[]> ToCoordinateDtos(LineString routeGeometry) =>
        routeGeometry.Coordinates
            .Select(coordinate => new[] { coordinate.X, coordinate.Y })
            .ToArray();

    private static RouteDetailDto ToRouteDetailDto(Route route, IReadOnlyList<double[]> routeGeometry)
    {
        var stops = route.RouteStops
            .OrderBy(x => x.StopOrder)
            .Select(x => new RouteStopDto(
                x.Id,
                x.Station.Id,
                x.Station.StationCode,
                x.Station.StationName,
                x.StopOrder,
                x.StandardTravelMin,
                x.IsPickupAllowed,
                x.IsDropoffAllowed))
            .ToArray();

        return new RouteDetailDto(
            route.Id,
            route.RouteCode,
            route.RouteName,
            route.RouteType,
            route.Description,
            route.BaseDistanceKm,
            route.EstimatedDurationMin,
            route.Status,
            route.IsBookable,
            stops,
            [],
            routeGeometry);
    }

    private static bool RouteCodesEqual(string? left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && string.Equals(NormalizeCode(left), NormalizeCode(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();

    private static ValidationException CreateValidationException(string propertyName, string message) =>
        new([new ValidationFailure(propertyName, message)]);
}
