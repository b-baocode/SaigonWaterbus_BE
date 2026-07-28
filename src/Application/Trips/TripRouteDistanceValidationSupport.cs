using FluentValidation.Results;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using AppValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

internal static class TripRouteDistanceValidationSupport
{
    public static void EnsureCompleteRegularRouteDistance(Route route, string propertyName)
    {
        var missingStops = GetMissingRegularRouteDistanceStops(route);
        if (missingStops.Count == 0)
        {
            return;
        }

        throw new AppValidationException([BuildFailure(route, propertyName, missingStops)]);
    }

    public static IReadOnlyList<ValidationFailure> BuildCompleteRegularRouteDistanceFailures(
        Route route,
        string propertyName)
    {
        var missingStops = GetMissingRegularRouteDistanceStops(route);
        return missingStops.Count == 0
            ? []
            : [BuildFailure(route, propertyName, missingStops)];
    }

    private static IReadOnlyList<RouteStop> GetMissingRegularRouteDistanceStops(Route route)
    {
        if (!string.Equals(route.RouteType, RouteTypes.Regular, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return route.RouteStops
            .OrderBy(x => x.StopOrder)
            .Where(x => x.StopOrder > 1 && x.DistanceFromPreviousKm is null or <= 0)
            .ToList();
    }

    private static ValidationFailure BuildFailure(
        Route route,
        string propertyName,
        IReadOnlyList<RouteStop> missingStops)
    {
        var missingStopNames = string.Join(", ", missingStops.Select(x =>
            $"{x.StopOrder}:{x.Station?.StationName ?? x.StationId.ToString()}"));

        return new ValidationFailure(
            propertyName,
            $"{DistanceFareSupport.MissingDistanceReason} Route '{route.RouteCode}' thiếu distanceFromPreviousKm ở stop: {missingStopNames}.");
    }
}
