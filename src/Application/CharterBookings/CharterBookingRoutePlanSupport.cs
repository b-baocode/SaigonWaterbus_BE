using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Routes;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record GetCharterBookingRouteCandidatesQuery(Guid BookingId)
    : IRequest<CharterBookingRouteCandidateResult>;

public sealed class GetCharterBookingRouteCandidatesQueryHandler
    : IRequestHandler<GetCharterBookingRouteCandidatesQuery, CharterBookingRouteCandidateResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCharterBookingRouteCandidatesQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CharterBookingRouteCandidateResult> Handle(
        GetCharterBookingRouteCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var booking = await CharterBookingRoutePlanSupport.LoadBookingWithItineraryAsync(
            _context,
            request.BookingId,
            cancellationToken);

        return await CharterBookingRoutePlanSupport.GetCandidatesAsync(
            _context,
            booking,
            cancellationToken);
    }
}

internal static class CharterBookingRoutePlanSupport
{
    private const decimal AverageSpeedKmh = 13m;
    private const double ProjectionThresholdMeters = 1_000;

    public static async Task<Booking> LoadBookingWithItineraryAsync(
        IApplicationDbContext context,
        Guid bookingId,
        CancellationToken cancellationToken) =>
        await CharterBookingQuerySupport.BuildBaseQuery(context)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

    public static async Task<CharterBookingRouteCandidateResult> GetCandidatesAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        var legs = BuildItineraryLegs(booking);
        var stationIds = legs
            .SelectMany(x => new[] { x.From.Id, x.To.Id })
            .Distinct()
            .ToArray();
        var routes = stationIds.Length < 2
            ? []
            : await context.Set<Route>()
                .AsNoTracking()
                .Include(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
                .Where(x => x.Status == "Active"
                    && x.RouteGeometry != null
                    && (x.RouteType == RouteTypes.CharterReference || x.RouteType == RouteTypes.SightseeingLoop)
                    && x.RouteStops.Any(stop => stationIds.Contains(stop.StationId)))
                .ToListAsync(cancellationToken);

        return new CharterBookingRouteCandidateResult(
            booking.Id,
            booking.BookingCode,
            legs.Select(leg => new CharterBookingRouteCandidateLegDto(
                    leg.LegOrder,
                    leg.From.Id,
                    leg.From.StationName,
                    leg.To.Id,
                    leg.To.StationName,
                    routes
                        .Select(route => TryBuildCandidate(route, leg))
                        .Where(candidate => candidate is not null)
                        .Select(candidate => candidate!)
                        .OrderByDescending(candidate => candidate.RouteType == RouteTypes.CharterReference)
                        .ThenBy(candidate => candidate.RouteCode)
                        .ToList()))
                .ToList());
    }

    public static async Task<Route?> ResolveSelectedRouteAsync(
        IApplicationDbContext context,
        Booking booking,
        IReadOnlyList<CharterBookingRoutePlanLegRequest>? routePlan,
        bool persist,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (routePlan is null || routePlan.Count == 0)
        {
            return null;
        }

        var legs = BuildItineraryLegs(booking);
        if (routePlan.Count != legs.Count)
        {
            throw CreateValidation("routePlan",
                $"routePlan phải có đúng {legs.Count} chặng theo itinerary của booking.");
        }

        var sourceRouteIds = routePlan.Select(x => x.RouteId).Distinct().ToArray();
        var sourceRoutes = await context.Set<Route>()
            .Include(x => x.RouteStops)
                .ThenInclude(x => x.Station)
            .Where(x => sourceRouteIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var routesById = sourceRoutes.ToDictionary(x => x.Id);

        var composedCoordinates = new List<Coordinate>();
        var composedStops = new List<ComposedStopDraft>();
        decimal totalDistanceKm = 0;
        var totalTravelMinutes = 0;
        var selectedLegs = new List<SelectedRouteLeg>();

        for (var i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            var requestedLeg = routePlan[i];
            EnsureLegMatchesItinerary(requestedLeg, leg, i);

            if (!routesById.TryGetValue(requestedLeg.RouteId, out var sourceRoute))
            {
                throw CreateValidation($"routePlan[{i}].routeId", "Route được chọn không tồn tại.");
            }

            var selectedLeg = ResolveSelectedRouteLeg(sourceRoute, leg, i);
            selectedLegs.Add(selectedLeg);

            AppendCoordinates(composedCoordinates, selectedLeg.SegmentCoordinates);
            totalDistanceKm += selectedLeg.DistanceKm;
            totalTravelMinutes += selectedLeg.TravelMinutes;

            if (i == 0)
            {
                composedStops.Add(new ComposedStopDraft(leg.From, null));
            }

            composedStops.Add(new ComposedStopDraft(
                leg.To,
                selectedLeg.TravelMinutes));
        }

        var route = new Route
        {
            RouteCode = persist
                ? await BuildUniqueRouteCodeAsync(context, booking, now, cancellationToken)
                : $"PREVIEW-{booking.BookingCode}",
            RouteName = CharterBookingRouteSupport.BuildCompactRouteName(booking),
            RouteType = RouteTypes.Charter,
            Description = $"Route charter ghép từ booking {booking.BookingCode}.",
            BaseDistanceKm = decimal.Round(totalDistanceKm, 2),
            EstimatedDurationMin = totalTravelMinutes,
            Status = "Active",
            IsBookable = false,
            RouteGeometry = composedCoordinates.Count >= 2
                ? new LineString(composedCoordinates.ToArray()) { SRID = 4326 }
                : null
        };

        route.RouteStops = composedStops
            .Select((stop, index) => new RouteStop
            {
                Route = route,
                RouteId = route.Id,
                Station = stop.Station,
                StationId = stop.Station.Id,
                StopOrder = index + 1,
                StandardTravelMin = stop.StandardTravelMin,
                IsPickupAllowed = index < composedStops.Count - 1,
                IsDropoffAllowed = index > 0
            })
            .ToList();

        if (persist)
        {
            context.Set<Route>().Add(route);
        }

        return route;
    }

    public static CharterBookingSelectedRouteDto? ToSelectedRouteDto(Route? route) =>
        route is null
            ? null
            : new CharterBookingSelectedRouteDto(
                route.Id,
                route.RouteCode,
                route.RouteName,
                route.RouteType);

    public static IReadOnlyList<ItineraryLeg> BuildItineraryLegs(Booking booking)
    {
        var points = BuildItineraryPoints(booking);
        if (points.Count < 2)
        {
            throw CreateValidation("routePlan",
                "Lộ trình charter cần ít nhất bến đi và một bến đến.");
        }

        return points
            .Zip(points.Skip(1), (from, to) => (from, to))
            .Select((x, index) => new ItineraryLeg(index + 1, x.from, x.to))
            .ToList();
    }

    private static List<Station> BuildItineraryPoints(Booking booking)
    {
        var points = new List<Station>();
        if (booking.FromStation is not null)
        {
            points.Add(booking.FromStation);
        }

        points.AddRange(booking.ItineraryStops
            .OrderBy(x => x.StopOrder)
            .Where(x => x.Station is not null)
            .Select(x => x.Station!));

        if (booking.ToStation is not null)
        {
            points.Add(booking.ToStation);
        }

        return points;
    }

    private static CharterBookingRouteCandidateDto? TryBuildCandidate(Route route, ItineraryLeg leg)
    {
        if (!TryResolveStopPair(route, leg.From.Id, leg.To.Id, out var fromStop, out var toStop))
        {
            return null;
        }

        if (!TryResolveSegment(route, leg.From, leg.To, out var segment))
        {
            return null;
        }

        return new CharterBookingRouteCandidateDto(
            route.Id,
            route.RouteCode,
            route.RouteName,
            route.RouteType,
            segment.DistanceKm,
            ResolveTravelMinutes(route, fromStop!, toStop!, segment.DistanceKm),
            fromStop!.StopOrder,
            toStop!.StopOrder);
    }

    private static SelectedRouteLeg ResolveSelectedRouteLeg(Route route, ItineraryLeg leg, int index)
    {
        if (route.Status != "Active")
        {
            throw CreateValidation($"routePlan[{index}].routeId", "Route được chọn phải đang Active.");
        }

        if (!string.Equals(route.RouteType, RouteTypes.CharterReference, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(route.RouteType, RouteTypes.SightseeingLoop, StringComparison.OrdinalIgnoreCase))
        {
            throw CreateValidation($"routePlan[{index}].routeId",
                "Route nguồn cho charter phải là CharterReference hoặc SightseeingLoop.");
        }

        if (!TryResolveStopPair(route, leg.From.Id, leg.To.Id, out var fromStop, out var toStop))
        {
            throw CreateValidation($"routePlan[{index}].routeId",
                "Route được chọn không đi đúng chiều từ bến đầu đến bến cuối của chặng.");
        }

        if (!TryResolveSegment(route, leg.From, leg.To, out var segment))
        {
            throw CreateValidation($"routePlan[{index}].routeId",
                "Route được chọn cần geometry hợp lệ và tọa độ bến nằm gần geometry.");
        }

        return new SelectedRouteLeg(
            route,
            fromStop!,
            toStop!,
            segment.Coordinates,
            segment.DistanceKm,
            ResolveTravelMinutes(route, fromStop!, toStop!, segment.DistanceKm));
    }

    private static void EnsureLegMatchesItinerary(
        CharterBookingRoutePlanLegRequest request,
        ItineraryLeg leg,
        int index)
    {
        if (request.FromStationId == leg.From.Id
            && request.ToStationId == leg.To.Id)
        {
            return;
        }

        throw CreateValidation($"routePlan[{index}]",
            "routePlan phải khớp đúng thứ tự from/to của itinerary booking.");
    }

    private static bool TryResolveStopPair(
        Route route,
        Guid fromStationId,
        Guid toStationId,
        out RouteStop? fromStop,
        out RouteStop? toStop)
    {
        var stops = route.RouteStops
            .OrderBy(x => x.StopOrder)
            .ToList();

        foreach (var candidateFrom in stops.Where(x => x.StationId == fromStationId))
        {
            var candidateTo = stops
                .Where(x => x.StationId == toStationId && x.StopOrder > candidateFrom.StopOrder)
                .OrderBy(x => x.StopOrder)
                .FirstOrDefault();
            if (candidateTo is not null)
            {
                fromStop = candidateFrom;
                toStop = candidateTo;
                return true;
            }
        }

        fromStop = null;
        toStop = null;
        return false;
    }

    private static bool TryResolveSegment(Route route, Station from, Station to, out RouteSegment segment)
    {
        segment = default;
        if (route.RouteGeometry is null
            || !from.Latitude.HasValue
            || !from.Longitude.HasValue
            || !to.Latitude.HasValue
            || !to.Longitude.HasValue)
        {
            return false;
        }

        var fromProjection = ProjectPointToLine(route.RouteGeometry, from);
        var toProjection = ProjectPointToLine(route.RouteGeometry, to);
        if (fromProjection.DistanceMeters > ProjectionThresholdMeters
            || toProjection.DistanceMeters > ProjectionThresholdMeters
            || toProjection.DistanceFromStartMeters <= fromProjection.DistanceFromStartMeters)
        {
            return false;
        }

        var coordinates = SliceLine(
            route.RouteGeometry,
            fromProjection.DistanceFromStartMeters,
            toProjection.DistanceFromStartMeters);
        var distanceKm = decimal.Round(
            (decimal)Math.Abs(toProjection.DistanceFromStartMeters - fromProjection.DistanceFromStartMeters) / 1000m,
            2);
        if (coordinates.Count < 2 || distanceKm <= 0)
        {
            return false;
        }

        segment = new RouteSegment(coordinates, distanceKm);
        return true;
    }

    private static int ResolveTravelMinutes(Route route, RouteStop fromStop, RouteStop toStop, decimal distanceKm)
    {
        var stops = route.RouteStops
            .OrderBy(x => x.StopOrder)
            .Where(x => x.StopOrder > fromStop.StopOrder && x.StopOrder <= toStop.StopOrder)
            .ToList();
        var configured = stops
            .Select(x => x.StandardTravelMin)
            .Where(x => x.HasValue && x.Value > 0)
            .Sum(x => x!.Value);

        return configured > 0
            ? configured
            : Math.Max(1, (int)Math.Ceiling(distanceKm / AverageSpeedKmh * 60));
    }

    private static RoutePointProjection ProjectPointToLine(LineString line, Station station)
    {
        var pointLatitude = (double)station.Latitude!.Value;
        var pointLongitude = (double)station.Longitude!.Value;
        var traversedMeters = 0d;
        var bestDistanceMeters = double.MaxValue;
        var bestDistanceFromStartMeters = 0d;

        for (var i = 0; i < line.NumPoints - 1; i++)
        {
            var start = line.GetPointN(i).Coordinate;
            var end = line.GetPointN(i + 1).Coordinate;
            var segmentMeters = RouteGeoJsonImportSupport.HaversineMeters(start.Y, start.X, end.Y, end.X);
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared > 0
                ? Math.Clamp(((pointLongitude - start.X) * dx + (pointLatitude - start.Y) * dy) / lengthSquared, 0d, 1d)
                : 0d;
            var projectedLongitude = start.X + (t * dx);
            var projectedLatitude = start.Y + (t * dy);
            var distanceMeters = RouteGeoJsonImportSupport.HaversineMeters(
                pointLatitude,
                pointLongitude,
                projectedLatitude,
                projectedLongitude);

            if (distanceMeters < bestDistanceMeters)
            {
                bestDistanceMeters = distanceMeters;
                bestDistanceFromStartMeters = traversedMeters + (segmentMeters * t);
            }

            traversedMeters += segmentMeters;
        }

        return new RoutePointProjection(bestDistanceMeters, bestDistanceFromStartMeters);
    }

    private static List<Coordinate> SliceLine(LineString line, double fromMeters, double toMeters)
    {
        var coordinates = new List<Coordinate>();
        var traversedMeters = 0d;

        for (var i = 0; i < line.NumPoints - 1; i++)
        {
            var start = line.GetPointN(i).Coordinate;
            var end = line.GetPointN(i + 1).Coordinate;
            var segmentMeters = RouteGeoJsonImportSupport.HaversineMeters(start.Y, start.X, end.Y, end.X);
            var segmentStart = traversedMeters;
            var segmentEnd = traversedMeters + segmentMeters;

            if (segmentEnd < fromMeters || segmentStart > toMeters)
            {
                traversedMeters = segmentEnd;
                continue;
            }

            var startRatio = segmentMeters <= 0 ? 0 : Math.Clamp((fromMeters - segmentStart) / segmentMeters, 0, 1);
            var endRatio = segmentMeters <= 0 ? 1 : Math.Clamp((toMeters - segmentStart) / segmentMeters, 0, 1);
            AppendCoordinate(coordinates, Interpolate(start, end, startRatio));
            if (endRatio >= 1)
            {
                AppendCoordinate(coordinates, new Coordinate(end.X, end.Y));
            }
            else
            {
                AppendCoordinate(coordinates, Interpolate(start, end, endRatio));
            }

            traversedMeters = segmentEnd;
        }

        return coordinates;
    }

    private static Coordinate Interpolate(Coordinate start, Coordinate end, double ratio) =>
        new(start.X + ((end.X - start.X) * ratio), start.Y + ((end.Y - start.Y) * ratio));

    private static void AppendCoordinates(List<Coordinate> target, IReadOnlyList<Coordinate> source)
    {
        foreach (var coordinate in source)
        {
            AppendCoordinate(target, coordinate);
        }
    }

    private static void AppendCoordinate(List<Coordinate> target, Coordinate coordinate)
    {
        if (target.Count == 0 || !target[^1].Equals2D(coordinate))
        {
            target.Add(new Coordinate(coordinate.X, coordinate.Y));
        }
    }

    private static async Task<string> BuildUniqueRouteCodeAsync(
        IApplicationDbContext context,
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var baseCode = CharterBookingRouteSupport.BuildCompactRouteCodeBase(booking.BookingCode);
        var code = baseCode.Length <= 50 ? baseCode : baseCode[..50];
        var suffix = 2;
        while (await context.Set<Route>().AnyAsync(x => x.RouteCode == code, cancellationToken))
        {
            var suffixText = $"-{suffix++}";
            code = baseCode.Length + suffixText.Length <= 50
                ? $"{baseCode}{suffixText}"
                : $"{baseCode[..(50 - suffixText.Length)]}{suffixText}";
        }

        return code;
    }

    private static ValidationException CreateValidation(string propertyName, string message) =>
        new([new ValidationFailure(propertyName, message)]);

    public sealed record ItineraryLeg(int LegOrder, Station From, Station To);

    private sealed record ComposedStopDraft(Station Station, int? StandardTravelMin);

    private sealed record SelectedRouteLeg(
        Route Route,
        RouteStop FromStop,
        RouteStop ToStop,
        IReadOnlyList<Coordinate> SegmentCoordinates,
        decimal DistanceKm,
        int TravelMinutes);

    private readonly record struct RouteSegment(IReadOnlyList<Coordinate> Coordinates, decimal DistanceKm);

    private sealed record RoutePointProjection(double DistanceMeters, double DistanceFromStartMeters);
}
