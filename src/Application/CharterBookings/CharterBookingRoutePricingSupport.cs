using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal sealed record CharterBookingRoutePricingEstimate(
    decimal UnitPrice,
    decimal ChargeableDurationValue,
    decimal SubtotalAmount,
    CharterBookingRouteEstimate RouteEstimate);

internal sealed record CharterBookingRouteEstimate(
    IReadOnlyList<CharterBookingRouteLegEstimate> Legs,
    decimal? TotalDistanceKm,
    decimal EstimatedTravelMinutes,
    int EstimatedStayMinutes,
    int FreeStayMinutes,
    int ChargeableStayMinutes,
    decimal EstimatedBufferMinutes,
    decimal EstimatedDurationMinutes,
    decimal ChargeableDurationMinutes,
    bool HasCompleteDistanceEstimate,
    bool HasCompleteTravelTimeEstimate);

internal sealed record CharterBookingRouteLegEstimate(
    int LegOrder,
    Guid FromStationId,
    string FromStationName,
    Guid ToStationId,
    string ToStationName,
    decimal? DistanceKm,
    decimal? TravelMinutes,
    Guid? MatchedRouteId,
    string? MatchedRouteCode,
    string? MatchedRouteName);

internal readonly record struct CharterBoatRentalPricePolicyKey(int NumberOfDecks, BoatRentalUnit RentalUnit);

internal static class CharterBookingRoutePricingSupport
{
    private const decimal DefaultAverageSpeedKmh = 13m;
    private const decimal DefaultBufferPercent = 0.10m;
    private const int DefaultFreeStayMinutesPerBooking = 30;
    private const double RouteProjectionThresholdMeters = 1_000;
    private const int DefaultRequestedDurationValue = 1;
    private static readonly int OperatingDayMinutes =
        (int)(CharterBookingTripSupport.OperatingDayEndTime - CharterBookingTripSupport.OperatingDayStartTime).TotalMinutes;
    private static CharterRouteEstimateOptions _defaultOptions = new();

    public static BoatRentalUnit ResolveRentalUnit(Booking booking) =>
        booking.RentalUnit ?? BoatRentalUnit.Hour;

    public static int ResolveRequestedDurationValue(Booking booking) =>
        Math.Max(DefaultRequestedDurationValue, booking.DurationValue.GetValueOrDefault(DefaultRequestedDurationValue));

    public static int ResolveHoldDurationValue(
        BoatRentalUnit rentalUnit,
        int requestedDurationValue,
        CharterBookingRouteEstimate routeEstimate)
    {
        if (rentalUnit == BoatRentalUnit.Day)
        {
            return requestedDurationValue;
        }

        var estimatedHours = routeEstimate.EstimatedDurationMinutes <= 0
            ? requestedDurationValue
            : (int)Math.Ceiling(routeEstimate.EstimatedDurationMinutes / 60m);

        return Math.Max(requestedDurationValue, Math.Max(DefaultRequestedDurationValue, estimatedHours));
    }

    public static async Task<IReadOnlyList<Route>> LoadRelatedRoutesAsync(
        IApplicationDbContext context,
        Booking booking,
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
                && (x.RouteType == RouteTypes.CharterReference || x.RouteType == RouteTypes.SightseeingLoop)
                && x.RouteStops.Any(stop => stationIds.Contains(stop.StationId)))
            .ToListAsync(cancellationToken);
    }

    public static CharterBookingRouteEstimate EstimateRoute(
        Booking booking,
        IReadOnlyCollection<Route>? relatedRoutes = null,
        CharterRouteEstimateOptions? options = null)
    {
        var resolvedOptions = ResolveOptions(options);
        var points = BuildRoutePoints(booking).ToArray();
        if (points.Length < 2)
        {
            var fallbackStayMinutes = EstimateStayMinutes(booking);
            var fallbackFreeStayMinutes = ResolveFreeStayMinutes(fallbackStayMinutes, resolvedOptions);
            var fallbackChargeableStayMinutes = ResolveChargeableStayMinutes(fallbackStayMinutes, fallbackFreeStayMinutes);

            return new CharterBookingRouteEstimate(
                [],
                null,
                0,
                fallbackStayMinutes,
                fallbackFreeStayMinutes,
                fallbackChargeableStayMinutes,
                0,
                fallbackStayMinutes,
                fallbackChargeableStayMinutes,
                HasCompleteDistanceEstimate: false,
                HasCompleteTravelTimeEstimate: false);
        }

        var routes = relatedRoutes ?? [];
        var resolvedLegs = points
            .Zip(points.Skip(1), (from, to) => (from, to))
            .Select((leg, index) =>
            {
                var matchedRoute = TryResolveRouteLeg(routes, leg.from, leg.to);
                var distanceKm = matchedRoute?.DistanceKm;
                var travelMinutes = matchedRoute?.TravelMinutes
                    ?? (distanceKm.HasValue
                        ? EstimateTravelMinutes(distanceKm.Value, resolvedOptions)
                        : null);

                return new
                {
                    Leg = new CharterBookingRouteLegEstimate(
                        index + 1,
                        leg.from.StationId,
                        leg.from.StationName,
                        leg.to.StationId,
                        leg.to.StationName,
                        distanceKm,
                        travelMinutes,
                        matchedRoute?.RouteId,
                        matchedRoute?.RouteCode,
                        matchedRoute?.RouteName),
                    UsesConfiguredTravelMinutes = matchedRoute?.TravelMinutes.HasValue == true
                };
            })
            .ToArray();
        var legs = resolvedLegs.Select(x => x.Leg).ToArray();

        var hasCompleteDistanceEstimate = legs.All(x => x.DistanceKm.HasValue);
        var hasCompleteTravelTimeEstimate = legs.All(x => x.TravelMinutes.HasValue);
        var totalDistanceKm = hasCompleteDistanceEstimate
            ? decimal.Round(legs.Sum(x => x.DistanceKm!.Value), 2)
            : (decimal?)null;
        var travelMinutes = hasCompleteTravelTimeEstimate
            ? legs.Sum(x => x.TravelMinutes!.Value)
            : 0m;
        var stayMinutes = EstimateStayMinutes(booking);
        var freeStayMinutes = ResolveFreeStayMinutes(stayMinutes, resolvedOptions);
        var chargeableStayMinutes = ResolveChargeableStayMinutes(stayMinutes, freeStayMinutes);
        var chargeableBaseMinutes = travelMinutes + chargeableStayMinutes;
        var allTravelTimesAreConfigured = resolvedLegs.Length > 0
            && resolvedLegs.All(x => x.UsesConfiguredTravelMinutes);
        var bufferMinutes = hasCompleteTravelTimeEstimate && !allTravelTimesAreConfigured
            ? ResolveBufferMinutes(chargeableBaseMinutes, resolvedOptions)
            : 0m;
        var totalMinutes = travelMinutes + stayMinutes + bufferMinutes;
        var chargeableDurationMinutes = chargeableBaseMinutes + bufferMinutes;

        return new CharterBookingRouteEstimate(
            legs,
            totalDistanceKm,
            travelMinutes,
            stayMinutes,
            freeStayMinutes,
            chargeableStayMinutes,
            bufferMinutes,
            totalMinutes,
            chargeableDurationMinutes,
            hasCompleteDistanceEstimate,
            hasCompleteTravelTimeEstimate);
    }

    public static CharterBookingRoutePricingEstimate EstimatePrice(
        Booking booking,
        Boat boat,
        BoatRentalUnit rentalUnit,
        int requestedDurationValue,
        IReadOnlyCollection<Route>? relatedRoutes = null,
        CharterRouteEstimateOptions? options = null,
        IReadOnlyDictionary<CharterBoatRentalPricePolicyKey, decimal>? rentalPricePolicies = null)
    {
        var routeEstimate = EstimateRoute(booking, relatedRoutes, options);
        var unitPrice = ResolveUnitPrice(boat, rentalUnit, rentalPricePolicies);
        var chargeableDurationValue = ResolveChargeableDurationValue(
            rentalUnit,
            requestedDurationValue,
            routeEstimate);
        var chargeableDurationMinutes = ResolveChargeableDurationMinutes(
            rentalUnit,
            requestedDurationValue,
            routeEstimate);
        var subtotalAmount = rentalUnit == BoatRentalUnit.Hour
            ? decimal.Round(unitPrice * chargeableDurationMinutes / 60m, 0, MidpointRounding.AwayFromZero)
            : unitPrice * chargeableDurationValue;

        return new CharterBookingRoutePricingEstimate(
            unitPrice,
            chargeableDurationValue,
            subtotalAmount,
            routeEstimate);
    }

    public static decimal ResolveChargeableDurationValue(
        BoatRentalUnit rentalUnit,
        int requestedDurationValue,
        CharterBookingRouteEstimate routeEstimate)
    {
        var requested = Math.Max(1, requestedDurationValue);
        if (!routeEstimate.HasCompleteTravelTimeEstimate || routeEstimate.EstimatedDurationMinutes <= 0)
        {
            return requested;
        }

        if (rentalUnit == BoatRentalUnit.Day)
        {
            return requested;
        }

        var requiredUnits = decimal.Round(
            ResolveChargeableDurationMinutes(
                rentalUnit,
                requestedDurationValue,
                routeEstimate) / 60m,
            3,
            MidpointRounding.AwayFromZero);

        return Math.Max(requested, Math.Max(1, requiredUnits));
    }

    private static decimal ResolveChargeableDurationMinutes(
        BoatRentalUnit rentalUnit,
        int requestedDurationValue,
        CharterBookingRouteEstimate routeEstimate)
    {
        var requestedMinutes = Math.Max(1, requestedDurationValue) * 60m;
        if (rentalUnit == BoatRentalUnit.Day)
        {
            return Math.Max(1, requestedDurationValue) * OperatingDayMinutes;
        }

        if (!routeEstimate.HasCompleteTravelTimeEstimate || routeEstimate.ChargeableDurationMinutes <= 0)
        {
            return requestedMinutes;
        }

        return Math.Max(requestedMinutes, routeEstimate.ChargeableDurationMinutes);
    }

    private static int ResolveFreeStayMinutes(int stayMinutes, CharterRouteEstimateOptions options) =>
        Math.Min(Math.Max(stayMinutes, 0), options.FreeStayMinutesPerBooking);

    private static int ResolveChargeableStayMinutes(int stayMinutes, int freeStayMinutes) =>
        Math.Max(stayMinutes - freeStayMinutes, 0);

    private static decimal ResolveBufferMinutes(decimal chargeableBaseMinutes, CharterRouteEstimateOptions options)
    {
        var percentBuffer = decimal.Ceiling(chargeableBaseMinutes * options.BufferPercent);
        return Math.Max(percentBuffer, options.MinimumBufferMinutes) + options.FixedBufferMinutes;
    }

    public static int GetFreeStayMinutesPerBooking(CharterRouteEstimateOptions? options = null) =>
        ResolveOptions(options).FreeStayMinutesPerBooking;

    public static void EnsureCanAutoPrice(
        BoatRentalUnit rentalUnit,
        CharterBookingRouteEstimate routeEstimate)
    {
        if (rentalUnit == BoatRentalUnit.Day)
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
            "Không thể tự tính giá thuê theo giờ vì booking chưa có route/GeoJSON/tọa độ hợp lệ cho các bến.")]);
    }

    public static CharterBookingRouteEstimateDto ToDto(
        CharterBookingRouteEstimate estimate,
        BoatRentalUnit rentalUnit,
        int requestedDurationValue,
        decimal? chargeableDurationValueOverride = null,
        decimal? chargeableDurationMinutesOverride = null,
        bool includeChargeablePricing = true)
    {
        decimal? chargeableDurationValue = includeChargeablePricing
            ? chargeableDurationValueOverride
                ?? ResolveChargeableDurationValue(rentalUnit, requestedDurationValue, estimate)
            : null;
        decimal? chargeableDurationMinutes = includeChargeablePricing
            ? chargeableDurationMinutesOverride
                ?? ResolveChargeableDurationMinutes(rentalUnit, requestedDurationValue, estimate)
            : null;

        var legDtos = estimate.Legs
            .Select(x => new CharterBookingRouteLegEstimateDto(
                x.LegOrder,
                x.FromStationId,
                x.FromStationName,
                x.ToStationId,
                x.ToStationName,
                x.DistanceKm,
                x.TravelMinutes,
                x.MatchedRouteId,
                x.MatchedRouteCode,
                x.MatchedRouteName))
            .ToArray();
        var matchedRoute = ResolveSingleMatchedRoute(estimate.Legs);

        return new CharterBookingRouteEstimateDto(
            legDtos,
            estimate.TotalDistanceKm,
            estimate.EstimatedTravelMinutes,
            estimate.EstimatedStayMinutes,
            estimate.FreeStayMinutes,
            estimate.ChargeableStayMinutes,
            estimate.EstimatedBufferMinutes,
            estimate.EstimatedDurationMinutes,
            chargeableDurationMinutes,
            chargeableDurationValue,
            includeChargeablePricing ? rentalUnit.ToString() : null,
            estimate.HasCompleteDistanceEstimate,
            estimate.HasCompleteTravelTimeEstimate,
            matchedRoute?.RouteId,
            matchedRoute?.RouteCode,
            matchedRoute?.RouteName);
    }

    public static async Task<IReadOnlyDictionary<CharterBoatRentalPricePolicyKey, decimal>> LoadRentalPricePoliciesAsync(
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<CharterBoatRentalPricePolicy>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            x => new CharterBoatRentalPricePolicyKey(x.NumberOfDecks, x.RentalUnit),
            x => x.UnitPrice);
    }

    private static decimal ResolveUnitPrice(
        Boat boat,
        BoatRentalUnit rentalUnit,
        IReadOnlyDictionary<CharterBoatRentalPricePolicyKey, decimal>? rentalPricePolicies)
    {
        var unitPrice = rentalUnit == BoatRentalUnit.Day
            ? boat.DailyRentalPrice
            : boat.HourlyRentalPrice;

        if (unitPrice.HasValue && unitPrice > 0)
        {
            return unitPrice.Value;
        }

        var policyKey = new CharterBoatRentalPricePolicyKey(boat.NumberOfDecks, rentalUnit);
        if (rentalPricePolicies is not null
            && rentalPricePolicies.TryGetValue(policyKey, out var policyUnitPrice)
            && policyUnitPrice > 0)
        {
            return policyUnitPrice;
        }

        if (!unitPrice.HasValue || unitPrice <= 0)
        {
            var unitName = rentalUnit == BoatRentalUnit.Day ? "ngày" : "giờ";
            throw new ValidationException([new ValidationFailure(nameof(rentalUnit),
                $"Tàu chưa cấu hình giá thuê theo {unitName}, và chưa có policy chung cho tàu {boat.NumberOfDecks} tầng.")]);
        }

        return unitPrice.Value;
    }

    private static IEnumerable<RoutePoint> BuildRoutePoints(Booking booking)
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

    private static int EstimateStayMinutes(Booking booking) =>
        booking.ItineraryStops.Sum(x => x.StayDurationMinutes);

    internal static decimal EstimateTravelMinutes(decimal distanceKm, CharterRouteEstimateOptions? options = null)
    {
        var resolvedOptions = ResolveOptions(options);
        return Math.Max(1m, decimal.Ceiling(distanceKm / resolvedOptions.AverageSpeedKmh * 60));
    }

    public static void ConfigureDefaults(CharterRouteEstimateOptions? options)
    {
        _defaultOptions = NormalizeOptions(options ?? new CharterRouteEstimateOptions());
    }

    private static CharterRouteEstimateOptions ResolveOptions(CharterRouteEstimateOptions? options) =>
        NormalizeOptions(options ?? _defaultOptions);

    private static CharterRouteEstimateOptions NormalizeOptions(CharterRouteEstimateOptions options) =>
        new()
        {
            AverageSpeedKmh = options.AverageSpeedKmh is > 0m
                ? options.AverageSpeedKmh
                : DefaultAverageSpeedKmh,
            BufferPercent = options.BufferPercent is >= 0m
                ? options.BufferPercent
                : DefaultBufferPercent,
            MinimumBufferMinutes = Math.Max(0, options.MinimumBufferMinutes),
            FixedBufferMinutes = Math.Max(0, options.FixedBufferMinutes),
            FreeStayMinutesPerBooking = options.FreeStayMinutesPerBooking >= 0
                ? options.FreeStayMinutesPerBooking
                : DefaultFreeStayMinutesPerBooking
        };

    private static MatchedRouteLeg? TryResolveRouteLeg(
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
                return new MatchedRouteLeg(
                    route.Id,
                    route.RouteCode,
                    route.RouteName,
                    decimal.Round((decimal)(distanceMeters / 1000d), 2),
                    route.RouteType == RouteTypes.Charter
                        ? null
                        : ResolveConfiguredTravelMinutes(route, from.StationId, to.StationId));
            }
        }

        return null;
    }

    private static decimal? ResolveConfiguredTravelMinutes(Route route, Guid fromStationId, Guid toStationId)
    {
        var orderedStops = route.RouteStops
            .OrderBy(stop => stop.StopOrder)
            .ToArray();
        var fromStop = orderedStops.FirstOrDefault(stop => stop.StationId == fromStationId);
        var toStop = orderedStops.FirstOrDefault(stop => stop.StationId == toStationId);
        if (fromStop is null || toStop is null || fromStop.StopOrder == toStop.StopOrder)
        {
            return null;
        }

        var minStopOrder = Math.Min(fromStop.StopOrder, toStop.StopOrder);
        var maxStopOrder = Math.Max(fromStop.StopOrder, toStop.StopOrder);
        var legMinutes = orderedStops
            .Where(stop => stop.StopOrder > minStopOrder && stop.StopOrder <= maxStopOrder)
            .Select(stop => stop.StandardTravelMin)
            .ToArray();

        if (legMinutes.Length == 0 || legMinutes.Any(minutes => minutes is not > 0m))
        {
            return null;
        }

        return decimal.Round(legMinutes.Sum(minutes => minutes!.Value), 2);
    }

    /// <summary>
    /// Cat doan geometry cua route nguon giua 2 ben (theo diem chieu len LineString),
    /// dao chieu neu ben di nam sau ben den tren route nguon. Tra ve null neu route
    /// khong co geometry, ben thieu toa do hoac diem chieu vuot nguong cho phep.
    /// </summary>
    internal static Coordinate[]? TryExtractLegGeometry(Route route, Station from, Station to)
    {
        if (route.RouteGeometry is null)
        {
            return null;
        }

        var fromPoint = RoutePoint.FromStation(from);
        var toPoint = RoutePoint.FromStation(to);
        if (!fromPoint.Latitude.HasValue
            || !fromPoint.Longitude.HasValue
            || !toPoint.Latitude.HasValue
            || !toPoint.Longitude.HasValue)
        {
            return null;
        }

        var fromProjection = ProjectPointToLine(route.RouteGeometry, fromPoint);
        var toProjection = ProjectPointToLine(route.RouteGeometry, toPoint);
        if (fromProjection.DistanceMeters > RouteProjectionThresholdMeters
            || toProjection.DistanceMeters > RouteProjectionThresholdMeters)
        {
            return null;
        }

        var reversed = fromProjection.DistanceFromStartMeters > toProjection.DistanceFromStartMeters;
        var startMeters = Math.Min(fromProjection.DistanceFromStartMeters, toProjection.DistanceFromStartMeters);
        var endMeters = Math.Max(fromProjection.DistanceFromStartMeters, toProjection.DistanceFromStartMeters);
        if (endMeters - startMeters <= 0)
        {
            return null;
        }

        var coordinates = ExtractSubLine(route.RouteGeometry, startMeters, endMeters);
        if (coordinates.Count < 2)
        {
            return null;
        }

        if (reversed)
        {
            coordinates.Reverse();
        }

        return [.. coordinates];
    }

    private static List<Coordinate> ExtractSubLine(LineString line, double startMeters, double endMeters)
    {
        var coordinates = new List<Coordinate>();
        var traversedMeters = 0d;

        for (var i = 0; i < line.NumPoints - 1; i++)
        {
            var start = line.GetPointN(i).Coordinate;
            var end = line.GetPointN(i + 1).Coordinate;
            var segmentMeters = HaversineMeters(start.Y, start.X, end.Y, end.X);
            var segmentEndMeters = traversedMeters + segmentMeters;

            if (segmentMeters > 0 && segmentEndMeters >= startMeters && traversedMeters <= endMeters)
            {
                if (coordinates.Count == 0)
                {
                    var t = Math.Clamp((startMeters - traversedMeters) / segmentMeters, 0d, 1d);
                    coordinates.Add(Interpolate(start, end, t));
                }

                if (segmentEndMeters >= endMeters)
                {
                    var t = Math.Clamp((endMeters - traversedMeters) / segmentMeters, 0d, 1d);
                    AddIfDistinct(coordinates, Interpolate(start, end, t));
                    break;
                }

                AddIfDistinct(coordinates, new Coordinate(end.X, end.Y));
            }

            traversedMeters = segmentEndMeters;
        }

        return coordinates;
    }

    private static Coordinate Interpolate(Coordinate start, Coordinate end, double t) =>
        new(start.X + ((end.X - start.X) * t), start.Y + ((end.Y - start.Y) * t));

    private static void AddIfDistinct(List<Coordinate> coordinates, Coordinate coordinate)
    {
        if (coordinates.Count == 0 || !coordinates[^1].Equals2D(coordinate))
        {
            coordinates.Add(coordinate);
        }
    }

    private static MatchedRouteSummary? ResolveSingleMatchedRoute(
        IReadOnlyCollection<CharterBookingRouteLegEstimate> legs)
    {
        if (legs.Count == 0 || legs.Any(x => !x.MatchedRouteId.HasValue))
        {
            return null;
        }

        var matchedRoutes = legs
            .Select(x => new MatchedRouteSummary(
                x.MatchedRouteId!.Value,
                x.MatchedRouteCode,
                x.MatchedRouteName))
            .Distinct()
            .ToArray();

        return matchedRoutes.Length == 1 ? matchedRoutes[0] : null;
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

    private sealed record MatchedRouteLeg(
        Guid RouteId,
        string RouteCode,
        string RouteName,
        decimal DistanceKm,
        decimal? TravelMinutes);

    private sealed record MatchedRouteSummary(
        Guid RouteId,
        string? RouteCode,
        string? RouteName);
}
