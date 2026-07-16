using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Routes;

public static class RoutePresentationSupport
{
    public const string BusLabel = "Bus";
    public const string GpsLabel = "GPS";
    public const string SightseeingLabel = "Sightseeing";
    public const string CharterLabel = "Charter";

    public static string ResolveLabel(string? routeType)
    {
        if (string.Equals(routeType, RouteTypes.CharterReference, StringComparison.OrdinalIgnoreCase))
        {
            return GpsLabel;
        }

        if (string.Equals(routeType, RouteTypes.SightseeingLoop, StringComparison.OrdinalIgnoreCase))
        {
            return SightseeingLabel;
        }

        if (string.Equals(routeType, RouteTypes.Charter, StringComparison.OrdinalIgnoreCase))
        {
            return CharterLabel;
        }

        return BusLabel;
    }

    public static bool IsSelectableForCharterQuote(Route route) =>
        string.Equals(route.Status, "Active", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(route.RouteType, RouteTypes.CharterReference, StringComparison.OrdinalIgnoreCase)
            || string.Equals(route.RouteType, RouteTypes.SightseeingLoop, StringComparison.OrdinalIgnoreCase));

    public static bool IsGeneratedForBooking(Route route) =>
        string.Equals(route.RouteType, RouteTypes.Charter, StringComparison.OrdinalIgnoreCase);
}
