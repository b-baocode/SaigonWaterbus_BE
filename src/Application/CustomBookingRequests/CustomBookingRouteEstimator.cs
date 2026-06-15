using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CustomBookingRouteEstimate(
    IReadOnlyList<CustomBookingRouteLegEstimate> Legs,
    decimal? TotalDistanceKm,
    decimal AverageSpeedKmh,
    decimal MaxSpeedKmh,
    int EstimatedTravelMinutes,
    int EstimatedStayMinutes,
    int BufferMinutes,
    int EstimatedDurationMinutes,
    DateOnly? EstimatedEndDate,
    TimeOnly? EstimatedEndTime,
    bool HasCompleteDistanceEstimate,
    bool HasCompleteTravelTimeEstimate);

public sealed record CustomBookingRouteLegEstimate(
    int LegOrder,
    string FromStationName,
    string ToStationName,
    decimal? DistanceKm,
    int? TravelMinutes);

public static class CustomBookingRouteEstimator
{
    public const decimal AverageSpeedKmh = 13m;
    public const decimal MaxSpeedKmh = 28m;
    public const decimal BufferPercent = 0.10m;

    public static CustomBookingRouteEstimate Estimate(
        CustomBookingRequest request,
        IReadOnlyCollection<RouteSegment>? routeSegments = null)
    {
        var points = BuildPoints(request).ToArray();
        var selectedRouteSegments = SelectBestRouteSegments(points, routeSegments ?? Array.Empty<RouteSegment>());
        return Estimate(
            points,
            request.ItineraryStops.Sum(x => x.StayDurationMinutes),
            request.DepartureDate,
            request.PreferredStartTime,
            selectedRouteSegments);
    }

    public static CustomBookingRouteEstimate Estimate(
        Station fromStation,
        IReadOnlyCollection<CustomBookingItineraryStop> itineraryStops,
        Station toStation,
        DateOnly departureDate,
        TimeOnly? startTime,
        Vessel? vessel,
        IReadOnlyCollection<RouteSegment>? routeSegments = null)
    {
        var points = BuildPoints(fromStation, itineraryStops, toStation).ToArray();
        var selectedRouteSegments = SelectBestRouteSegments(points, routeSegments ?? Array.Empty<RouteSegment>());
        return Estimate(
            points,
            itineraryStops.Sum(x => x.StayDurationMinutes),
            departureDate,
            startTime,
            selectedRouteSegments);
    }

    public static int EstimateTravelMinutes(decimal distanceKm) =>
        Math.Max(1, (int)Math.Ceiling(distanceKm / AverageSpeedKmh * 60));

    public static string FormatDuration(int minutes)
    {
        if (minutes <= 0)
        {
            return "0 phút";
        }

        var hours = minutes / 60;
        var remainingMinutes = minutes % 60;

        return (hours, remainingMinutes) switch
        {
            (0, _) => $"{remainingMinutes} phút",
            (_, 0) => $"{hours} giờ",
            _ => $"{hours} giờ {remainingMinutes} phút"
        };
    }

    private static CustomBookingRouteEstimate Estimate(
        IReadOnlyList<RoutePoint> points,
        int stayMinutes,
        DateOnly departureDate,
        TimeOnly? startTime,
        IReadOnlyCollection<RouteSegment> routeSegments)
    {
        var legs = points
            .Zip(points.Skip(1), (from, to) => (from, to))
            .Select((leg, index) =>
            {
                var matchedSegment = FindRouteSegment(routeSegments, leg.from.StationId, leg.to.StationId);
                var distanceKm = matchedSegment?.DistanceKm ?? DistanceKm(leg.from, leg.to);
                var travelMinutes = distanceKm.HasValue
                    ? EstimateTravelMinutes(distanceKm.Value)
                    : (int?)null;

                return new CustomBookingRouteLegEstimate(
                    index + 1,
                    leg.from.Name,
                    leg.to.Name,
                    distanceKm,
                    travelMinutes);
            })
            .ToArray();

        var hasCompleteDistanceEstimate = legs.All(x => x.DistanceKm.HasValue);
        var hasCompleteTravelTimeEstimate = legs.All(x => x.TravelMinutes.HasValue);
        var totalDistanceKm = hasCompleteDistanceEstimate
            ? legs.Sum(x => x.DistanceKm!.Value)
            : (decimal?)null;
        var travelMinutes = hasCompleteTravelTimeEstimate
            ? legs.Sum(x => x.TravelMinutes!.Value)
            : 0;
        var bufferMinutes = hasCompleteTravelTimeEstimate
            ? (int)Math.Ceiling((travelMinutes + stayMinutes) * BufferPercent)
            : 0;
        var totalMinutes = travelMinutes + stayMinutes + bufferMinutes;

        var endDate = (DateOnly?)null;
        var endTime = (TimeOnly?)null;
        if (hasCompleteTravelTimeEstimate && startTime.HasValue)
        {
            var startDateTime = departureDate.ToDateTime(startTime.Value);
            var endDateTime = startDateTime.AddMinutes(totalMinutes);
            endDate = DateOnly.FromDateTime(endDateTime);
            endTime = TimeOnly.FromDateTime(endDateTime);
        }

        return new CustomBookingRouteEstimate(
            legs,
            totalDistanceKm.HasValue ? decimal.Round(totalDistanceKm.Value, 2) : null,
            AverageSpeedKmh,
            MaxSpeedKmh,
            travelMinutes,
            stayMinutes,
            bufferMinutes,
            totalMinutes,
            endDate,
            endTime,
            hasCompleteDistanceEstimate,
            hasCompleteTravelTimeEstimate);
    }

    private static IEnumerable<RoutePoint> BuildPoints(CustomBookingRequest request)
    {
        yield return new RoutePoint(
            request.FromStation?.StationName ?? request.FromLocation,
            request.FromStationId,
            request.FromStation?.Latitude,
            request.FromStation?.Longitude);

        foreach (var stop in request.ItineraryStops.OrderBy(x => x.StopOrder))
        {
            yield return new RoutePoint(stop.Station.StationName, stop.StationId, stop.Station.Latitude, stop.Station.Longitude);
        }

        yield return new RoutePoint(
            request.ToStation?.StationName ?? request.ToLocation,
            request.ToStationId,
            request.ToStation?.Latitude,
            request.ToStation?.Longitude);
    }

    private static IEnumerable<RoutePoint> BuildPoints(
        Station fromStation,
        IReadOnlyCollection<CustomBookingItineraryStop> itineraryStops,
        Station toStation)
    {
        yield return new RoutePoint(fromStation.StationName, fromStation.Id, fromStation.Latitude, fromStation.Longitude);

        foreach (var stop in itineraryStops.OrderBy(x => x.StopOrder))
        {
            yield return new RoutePoint(stop.Station.StationName, stop.StationId, stop.Station.Latitude, stop.Station.Longitude);
        }

        yield return new RoutePoint(toStation.StationName, toStation.Id, toStation.Latitude, toStation.Longitude);
    }

    private static RouteSegment? FindRouteSegment(
        IReadOnlyCollection<RouteSegment> routeSegments,
        Guid? fromStationId,
        Guid? toStationId)
    {
        if (!fromStationId.HasValue || !toStationId.HasValue)
        {
            return null;
        }

        var exactDirection = routeSegments
            .FirstOrDefault(x =>
                x.FromStationId == fromStationId.Value && x.ToStationId == toStationId.Value);

        return exactDirection
            ?? routeSegments
                .FirstOrDefault(x =>
                    x.FromStationId == toStationId.Value && x.ToStationId == fromStationId.Value);
    }

    private static IReadOnlyList<RouteSegment> SelectBestRouteSegments(
        IReadOnlyList<RoutePoint> points,
        IReadOnlyCollection<RouteSegment> routeSegments)
    {
        if (routeSegments.Count == 0 || points.Count < 2)
        {
            return Array.Empty<RouteSegment>();
        }

        var legs = points
            .Zip(points.Skip(1), (from, to) => new { From = from.StationId, To = to.StationId })
            .Where(x => x.From.HasValue && x.To.HasValue)
            .Select(x => (FromStationId: x.From!.Value, ToStationId: x.To!.Value))
            .ToArray();

        if (legs.Length == 0)
        {
            return Array.Empty<RouteSegment>();
        }

        var rankedGroups = routeSegments
            .GroupBy(x => x.RouteId)
            .Select(group =>
            {
                var segments = group.ToArray();
                var exactMatches = legs.Count(leg => segments.Any(segment =>
                    segment.FromStationId == leg.FromStationId && segment.ToStationId == leg.ToStationId));
                var reverseMatches = legs.Count(leg => segments.Any(segment =>
                    segment.FromStationId == leg.ToStationId && segment.ToStationId == leg.FromStationId));
                var coveredLegs = legs.Count(leg => segments.Any(segment =>
                    (segment.FromStationId == leg.FromStationId && segment.ToStationId == leg.ToStationId)
                    || (segment.FromStationId == leg.ToStationId && segment.ToStationId == leg.FromStationId)));
                var score = coveredLegs * 100 + exactMatches * 10 + reverseMatches;

                return new
                {
                    Segments = segments,
                    Score = score,
                    ExactMatches = exactMatches,
                    CoveredLegs = coveredLegs
                };
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.CoveredLegs)
            .ThenByDescending(x => x.ExactMatches)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Segments.Min(segment => segment.RouteId))
            .ToArray();

        var bestGroup = rankedGroups.FirstOrDefault();
        if (bestGroup is null)
        {
            return Array.Empty<RouteSegment>();
        }

        var bestSegments = bestGroup.Segments.ToHashSet(ReferenceEqualityComparer.Instance);
        return bestGroup.Segments
            .OrderBy(x => x.SegmentOrder)
            .Concat(routeSegments
                .Where(x => !bestSegments.Contains(x))
                .OrderBy(x => x.SegmentOrder))
            .ToArray();
    }

    private static decimal? DistanceKm(RoutePoint from, RoutePoint to)
    {
        if (!from.Latitude.HasValue
            || !from.Longitude.HasValue
            || !to.Latitude.HasValue
            || !to.Longitude.HasValue)
        {
            return null;
        }

        const double earthRadiusKm = 6371.0;
        var fromLat = DegreesToRadians((double)from.Latitude.Value);
        var fromLng = DegreesToRadians((double)from.Longitude.Value);
        var toLat = DegreesToRadians((double)to.Latitude.Value);
        var toLng = DegreesToRadians((double)to.Longitude.Value);
        var dLat = toLat - fromLat;
        var dLng = toLng - fromLng;

        var a = Math.Pow(Math.Sin(dLat / 2), 2)
            + Math.Cos(fromLat) * Math.Cos(toLat) * Math.Pow(Math.Sin(dLng / 2), 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return decimal.Round((decimal)(earthRadiusKm * c), 2);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private sealed record RoutePoint(string Name, Guid? StationId, decimal? Latitude, decimal? Longitude);
}
