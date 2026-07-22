using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Trips;

internal static class TripMediaSupport
{
    private const int MaxBoatImages = 3;
    private const int MaxStationImages = 6;

    public static TripBoatDto? ToBoatDto(Boat? boat, int capacity)
    {
        if (boat is null)
        {
            return null;
        }

        var imageUrls = CreateBoatImageUrls(boat);
        return new TripBoatDto(
            boat.Id,
            boat.Name,
            boat.Code,
            capacity,
            boat.Status.ToString(),
            imageUrls.FirstOrDefault(),
            imageUrls,
            boat.RegistrationNumber,
            boat.ServiceType.ToString(),
            boat.NumberOfDecks,
            boat.MaxSpeedKmh,
            boat.YearBuilt,
            boat.Description);
    }

    public static TripRouteEndpointDto? ResolveFromStation(Trip trip)
    {
        var routeStop = trip.Route.RouteStops
            .OrderBy(x => x.StopOrder)
            .FirstOrDefault();
        if (routeStop?.Station is not null)
        {
            return ToEndpointDto(routeStop.Station);
        }

        var tripStop = trip.TripStops
            .OrderBy(x => x.StopOrder)
            .FirstOrDefault();
        return tripStop?.Station is null ? null : ToEndpointDto(tripStop.Station);
    }

    public static TripRouteEndpointDto? ResolveToStation(Trip trip)
    {
        var routeStop = trip.Route.RouteStops
            .OrderByDescending(x => x.StopOrder)
            .FirstOrDefault();
        if (routeStop?.Station is not null)
        {
            return ToEndpointDto(routeStop.Station);
        }

        var tripStop = trip.TripStops
            .OrderByDescending(x => x.StopOrder)
            .FirstOrDefault();
        return tripStop?.Station is null ? null : ToEndpointDto(tripStop.Station);
    }

    public static TripRouteEndpointDto ToEndpointDto(Station station)
    {
        var imageUrls = CreateStationImageUrls(station);
        return new TripRouteEndpointDto(
            station.Id,
            station.StationCode,
            station.StationName,
            imageUrls.FirstOrDefault(),
            imageUrls,
            station.Address,
            station.Latitude,
            station.Longitude,
            station.HasWaitingArea,
            station.HasParking,
            station.HasTicketCounter);
    }

    public static IReadOnlyList<string> CreateBoatImageUrls(Boat? boat)
    {
        if (boat is null)
        {
            return [];
        }

        return boat.ImageUrls.Length > 0
            ? boat.ImageUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxBoatImages)
                .ToArray()
            : string.IsNullOrWhiteSpace(boat.ImageUrl)
                ? []
                : [boat.ImageUrl.Trim()];
    }

    public static IReadOnlyList<string> CreateStationImageUrls(Station? station)
    {
        if (station is null)
        {
            return [];
        }

        return station.ImageUrls.Length > 0
            ? station.ImageUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxStationImages)
                .ToArray()
            : string.IsNullOrWhiteSpace(station.ImageUrl)
                ? []
                : [station.ImageUrl.Trim()];
    }
}
