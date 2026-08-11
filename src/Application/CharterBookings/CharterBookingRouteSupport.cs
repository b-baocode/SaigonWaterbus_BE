using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingRouteSupport
{
    private const string InactiveStatus = "Inactive";

    public static string BuildCompactRouteCodeBase(string bookingCode)
    {
        var normalized = NormalizeRouteCodePart(bookingCode);
        var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var token = parts.Length >= 3 && parts[0] == "CB"
            ? parts[^1]
            : normalized;

        return $"CH-{token}";
    }

    public static string BuildCompactRouteName(Booking booking)
    {
        var from = booking.FromStation?.StationName ?? "Điểm đi";
        var to = booking.ToStation?.StationName ?? "Điểm đến";
        var token = BuildCompactRouteCodeBase(booking.BookingCode).Replace("CH-", string.Empty, StringComparison.Ordinal);
        var value = $"Charter {token}: {from} - {to}";
        return value.Length <= 150 ? value : value[..150];
    }

    public static async Task DeactivateOwnedRouteAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (!booking.CharterRouteId.HasValue)
        {
            return;
        }

        var route = booking.CharterRoute
            ?? await context.Set<Route>()
                .FirstOrDefaultAsync(x => x.Id == booking.CharterRouteId.Value, cancellationToken);

        if (route is null || !IsOwnedComposedRoute(route, booking))
        {
            return;
        }

        var usedByOtherBooking = await context.Set<Booking>()
            .AsNoTracking()
            .AnyAsync(x => x.Id != booking.Id
                && x.BookingType == Booking.CharterBookingType
                && x.CharterRouteId == route.Id,
                cancellationToken);

        var usedByOpenBooking = await context.Set<Booking>()
            .AsNoTracking()
            .AnyAsync(x => x.Id != booking.Id
                && x.BookingType == Booking.CharterBookingType
                && x.CharterRouteId == route.Id
                && x.BookingStatus != BookingStatus.Cancelled
                && x.BookingStatus != BookingStatus.Expired
                && x.BookingStatus != BookingStatus.Completed,
                cancellationToken);

        var hasTrips = await context.Set<Trip>()
            .AsNoTracking()
            .AnyAsync(x => x.RouteId == route.Id, cancellationToken);

        if (!hasTrips && !usedByOtherBooking && ShouldDeleteUnusedOwnedRoute(booking))
        {
            booking.CharterRouteId = null;
            booking.CharterRoute = null;
            context.Set<Route>().Remove(route);
            return;
        }

        if (!usedByOpenBooking)
        {
            route.Status = InactiveStatus;
        }
    }

    private static bool ShouldDeleteUnusedOwnedRoute(Booking booking) =>
        booking.BookingStatus is not BookingStatus.Completed;

    private static bool IsOwnedComposedRoute(Route route, Booking booking)
    {
        if (!string.Equals(route.RouteType, RouteTypes.Charter, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(route.RouteType, RouteTypes.CharterReference, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var description = route.Description ?? string.Empty;
        if (description.Contains(booking.BookingCode, StringComparison.OrdinalIgnoreCase)
            && (description.StartsWith("Route charter", StringComparison.OrdinalIgnoreCase)
                || description.StartsWith("Route tao tu lo trinh charter booking", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var expectedCodePrefix = BuildCompactRouteCodeBase(booking.BookingCode);
        return route.RouteCode.StartsWith(expectedCodePrefix, StringComparison.OrdinalIgnoreCase)
            && route.RouteName.Contains("Charter", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRouteCodePart(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        var chars = normalized
            .Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-')
            .ToArray();
        return chars.Length == 0 ? "BOOKING" : new string(chars);
    }
}
