using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CustomBookings;

internal sealed record CustomBookingRoutePricingEstimate(
    decimal UnitPrice,
    int ChargeableDurationValue,
    decimal SubtotalAmount,
    CustomBookingRouteEstimate RouteEstimate);

internal sealed record CustomBookingRouteEstimate(
    IReadOnlyList<CustomBookingRouteLegEstimate> Legs,
    decimal? TotalDistanceKm,
    int EstimatedTravelMinutes,
    int EstimatedStayMinutes,
    int EstimatedBufferMinutes,
    int EstimatedDurationMinutes,
    bool HasCompleteDistanceEstimate,
    bool HasCompleteTravelTimeEstimate);

internal sealed record CustomBookingRouteLegEstimate(
    int LegOrder,
    string FromStationName,
    string ToStationName,
    decimal? DistanceKm,
    int? TravelMinutes);

internal static class CustomBookingRoutePricingSupport
{
    private const decimal AverageSpeedKmh = 13m;
    private const decimal BufferPercent = 0.10m;
    private const int MinutesPerChargeableDay = 12 * 60;
    private const double RouteProjectionThresholdMeters = 1_000;

    public static async Task<IReadOnlyList<Route>> LoadRelatedRoutesAsync(
        IApplicationDbContext context,
        CustomBooking booking,
        CancellationToken cancellationToken)
    {
        var stationIds = BuildRoutePoints(booking)
            .Select(x => x.StationId)
            .Distinct()
            .ToArray();

        if (stationIds.Length < 2)
        {
            return [];
        }

        return await context.Set<Route>()
            .AsNoTracking()
            .Include(x => x.RouteStops)
            .Where(x => x.RouteGeometry != null
                && x.RouteStops.Any(stop => stationIds.Contains(stop.StationId)))
            .ToListAsync(cancellationToken);
    }

    public static CustomBookingRouteEstimate EstimateRoute(
        CustomBooking booking,
        IReadOnlyCollection<Route>? relatedRoutes = null)
    {
        var points = BuildRoutePoints(booking).ToArray();
        if (points.Length < 2)
        {
            return new CustomBookingRouteEstimate(
                [],
                null,
                0,
                EstimateStayMinutes(booking),
                0,
                EstimateStayMinutes(booking),
                HasCompleteDistanceEstimate: false,
                HasCompleteTravelTimeEstimate: false);
        }

        var routes = relatedRoutes ?? [];
        var legs = points
            .Zip(points.Skip(1), (from, to) => (from, to))
            .Select((leg, index) =>
            {
                var distanceKm = TryMeasureRouteDistanceKm(routes, leg.from, leg.to)
                    ?? HaversineDistanceKm(leg.from, leg.to);
                var travelMinutes = distanceKm.HasValue
                    ? EstimateTravelMinutes(distanceKm.Value)
                    : (int?)null;

                return new CustomBookingRouteLegEstimate(
                    index + 1,
                    leg.from.StationName,
                    leg.to.StationName,
                    distanceKm,
                    travelMinutes);
            })
            .ToArray();

        var hasCompleteDistanceEstimate = legs.All(x => x.DistanceKm.HasValue);
        var hasCompleteTravelTimeEstimate = legs.All(x => x.TravelMinutes.HasValue);
        var totalDistanceKm = hasCompleteDistanceEstimate
            ? decimal.Round(legs.Sum(x => x.DistanceKm!.Value), 2)
            : (decimal?)null;
        var travelMinutes = hasCompleteTravelTimeEstimate
            ? legs.Sum(x => x.TravelMinutes!.Value)
            : 0;
        var stayMinutes = EstimateStayMinutes(booking);
        var bufferMinutes = hasCompleteTravelTimeEstimate
            ? (int)Math.Ceiling((travelMinutes + stayMinutes) * (double)BufferPercent)
            : 0;
        var totalMinutes = travelMinutes + stayMinutes + bufferMinutes;

        return new CustomBookingRouteEstimate(
            legs,
            totalDistanceKm,
            travelMinutes,
            stayMinutes,
            bufferMinutes,
            totalMinutes,
            hasCompleteDistanceEstimate,
            hasCompleteTravelTimeEstimate);
    }

    public static CustomBookingRoutePricingEstimate EstimatePrice(
        CustomBooking booking,
        Vessel vessel,
        VesselRentalUnit rentalUnit,
        int requestedDurationValue,
        IReadOnlyCollection<Route>? relatedRoutes = null)
    {
        var routeEstimate = EstimateRoute(booking, relatedRoutes);
        var unitPrice = ResolveUnitPrice(vessel, rentalUnit);
        var chargeableDurationValue = ResolveChargeableDurationValue(
            rentalUnit,
            requestedDurationValue,
            routeEstimate);

        return new CustomBookingRoutePricingEstimate(
            unitPrice,
            chargeableDurationValue,
            unitPrice * chargeableDurationValue,
            routeEstimate);
    }

    public static int ResolveChargeableDurationValue(
        VesselRentalUnit rentalUnit,
        int requestedDurationValue,
        CustomBookingRouteEstimate routeEstimate)
    {
        var requested = Math.Max(1, requestedDurationValue);
        if (!routeEstimate.HasCompleteTravelTimeEstimate || routeEstimate.EstimatedDurationMinutes <= 0)
        {
            return requested;
        }

        var requiredUnits = rentalUnit == VesselRentalUnit.Day
            ? (int)Math.Ceiling(routeEstimate.EstimatedDurationMinutes / (double)MinutesPerChargeableDay)
            : (int)Math.Ceiling(routeEstimate.EstimatedDurationMinutes / 60d);

        return Math.Max(requested, Math.Max(1, requiredUnits));
    }

    public static void EnsureCanAutoPrice(
        VesselRentalUnit rentalUnit,
        CustomBookingRouteEstimate routeEstimate)
    {
        if (rentalUnit == VesselRentalUnit.Day)
        {
            return;
        }

        if (routeEstimate.HasCompleteDistanceEstimate
            && routeEstimate.HasCompleteTravelTimeEstimate
            && routeEstimate.TotalDistanceKm.HasValue)
        {
            return;
        }

        throw new ValidationException([new ValidationFailure("SubtotalAmount",
            "Không thể tự tính giá thuê theo giờ vì booking chưa có đủ dữ liệu quãng đường/thời gian. Vui lòng cập nhật GeoJSON/tọa độ cho các bến hoặc nhập subtotalAmount thủ công.")]);
    }

    public static CustomBookingRouteEstimateDto ToDto(
        CustomBookingRouteEstimate estimate,
        VesselRentalUnit rentalUnit,
        int requestedDurationValue)
    {
        return new CustomBookingRouteEstimateDto(
            estimate.Legs
                .Select(x => new CustomBookingRouteLegEstimateDto(
                    x.LegOrder,
                    x.FromStationName,
                    x.ToStationName,
                    x.DistanceKm,
                    x.TravelMinutes))
                .ToArray(),
            estimate.TotalDistanceKm,
            estimate.EstimatedTravelMinutes,
            estimate.EstimatedStayMinutes,
            estimate.EstimatedBufferMinutes,
            estimate.EstimatedDurationMinutes,
            ResolveChargeableDurationValue(rentalUnit, requestedDurationValue, estimate),
            rentalUnit.ToString(),
            estimate.HasCompleteDistanceEstimate,
            estimate.HasCompleteTravelTimeEstimate);
    }

    private static decimal ResolveUnitPrice(Vessel vessel, VesselRentalUnit rentalUnit)
    {
        var unitPrice = rentalUnit == VesselRentalUnit.Day
            ? vessel.DailyRentalPrice
            : vessel.HourlyRentalPrice;

        if (!unitPrice.HasValue || unitPrice <= 0)
        {
            var unitName = rentalUnit == VesselRentalUnit.Day ? "ngày" : "giờ";
            throw new ValidationException([new ValidationFailure(nameof(rentalUnit),
                $"Tàu chưa cấu hình giá thuê theo {unitName}.")]);
        }

        return unitPrice.Value;
    }

    private static IEnumerable<RoutePoint> BuildRoutePoints(CustomBooking booking)
    {
        if (booking.FromStation is not null)
        {
            yield return RoutePoint.FromStation(booking.FromStation);
        }

        foreach (var stop in booking.ItineraryStops.OrderBy(x => x.StopOrder))
        {
            if (stop.Station is not null)
            {
                yield return RoutePoint.FromStation(stop.Station);
            }
        }

        if (booking.ToStation is not null)
        {
            yield return RoutePoint.FromStation(booking.ToStation);
        }
    }

    private static int EstimateStayMinutes(CustomBooking booking) =>
        booking.ItineraryStops.Sum(x => x.StayDurationMinutes);

    private static int EstimateTravelMinutes(decimal distanceKm) =>
        Math.Max(1, (int)Math.Ceiling(distanceKm / AverageSpeedKmh * 60));

    private static decimal? TryMeasureRouteDistanceKm(
        IReadOnlyCollection<Route> routes,
        RoutePoint from,
        RoutePoint to)
    {
        if (!from.Latitude.HasValue
            || !from.Longitude.HasValue
            || !to.Latitude.HasValue
            || !to.Longitude.HasValue)
        {
            return null;
        }

        var candidates = routes
            .Where(route => route.RouteGeometry is not null
                && route.RouteStops.Any(stop => stop.StationId == from.StationId)
                && route.RouteStops.Any(stop => stop.StationId == to.StationId))
            .ToArray();

        foreach (var route in candidates)
        {
            var fromProjection = ProjectPointToLine(route.RouteGeometry!, from);
            var toProjection = ProjectPointToLine(route.RouteGeometry!, to);
            if (fromProjection.DistanceMeters > RouteProjectionThresholdMeters
                || toProjection.DistanceMeters > RouteProjectionThresholdMeters)
            {
                continue;
            }

            var distanceMeters = Math.Abs(toProjection.DistanceFromStartMeters - fromProjection.DistanceFromStartMeters);
            if (distanceMeters > 0)
            {
                return decimal.Round((decimal)(distanceMeters / 1000d), 2);
            }
        }

        return null;
    }

    private static RoutePointProjection ProjectPointToLine(LineString line, RoutePoint point)
    {
        var pointLatitude = (double)point.Latitude!.Value;
        var pointLongitude = (double)point.Longitude!.Value;
        var totalMeters = CalculateLengthMeters(line);
        var traversedMeters = 0d;
        var bestDistanceMeters = double.MaxValue;
        var bestDistanceFromStartMeters = 0d;

        for (var i = 0; i < line.NumPoints - 1; i++)
        {
            var start = line.GetPointN(i).Coordinate;
            var end = line.GetPointN(i + 1).Coordinate;
            var segmentMeters = HaversineMeters(start.Y, start.X, end.Y, end.X);
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared > 0
                ? Math.Clamp(((pointLongitude - start.X) * dx + (pointLatitude - start.Y) * dy) / lengthSquared, 0d, 1d)
                : 0d;
            var projectedLongitude = start.X + (t * dx);
            var projectedLatitude = start.Y + (t * dy);
            var distanceMeters = HaversineMeters(pointLatitude, pointLongitude, projectedLatitude, projectedLongitude);

            if (distanceMeters < bestDistanceMeters)
            {
                bestDistanceMeters = distanceMeters;
                bestDistanceFromStartMeters = traversedMeters + (segmentMeters * t);
            }

            traversedMeters += segmentMeters;
        }

        return new RoutePointProjection(
            totalMeters > 0 ? Math.Clamp(bestDistanceFromStartMeters / totalMeters, 0d, 1d) : 0d,
            bestDistanceFromStartMeters,
            bestDistanceMeters);
    }

    private static decimal? HaversineDistanceKm(RoutePoint from, RoutePoint to)
    {
        if (!from.Latitude.HasValue
            || !from.Longitude.HasValue
            || !to.Latitude.HasValue
            || !to.Longitude.HasValue)
        {
            return null;
        }

        return decimal.Round((decimal)(HaversineMeters(
            (double)from.Latitude.Value,
            (double)from.Longitude.Value,
            (double)to.Latitude.Value,
            (double)to.Longitude.Value) / 1000d), 2);
    }

    private static double CalculateLengthMeters(LineString line)
    {
        var meters = 0d;
        for (var i = 0; i < line.NumPoints - 1; i++)
        {
            meters += HaversineMeters(
                line.GetPointN(i).Y,
                line.GetPointN(i).X,
                line.GetPointN(i + 1).Y,
                line.GetPointN(i + 1).X);
        }

        return meters;
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMeters = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private sealed record RoutePoint(
        Guid StationId,
        string StationName,
        decimal? Latitude,
        decimal? Longitude)
    {
        public static RoutePoint FromStation(Station station) =>
            new(station.Id, station.StationName, station.Latitude, station.Longitude);
    }

    private sealed record RoutePointProjection(
        double Fraction,
        double DistanceFromStartMeters,
        double DistanceMeters);
}
