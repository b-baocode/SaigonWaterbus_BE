using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Tracking;

public static class TrackingCurrentStationResolver
{
    public static Station? Resolve(
        IEnumerable<Station> stations,
        string? requestedStationCode,
        decimal lat,
        decimal lng,
        double radiusKm)
    {
        if (radiusKm <= 0)
        {
            return null;
        }

        var candidates = stations
            .Where(x => x.Latitude.HasValue && x.Longitude.HasValue)
            .Select(x => new StationCandidate(
                x,
                CalculateDistanceKm(lat, lng, x.Latitude!.Value, x.Longitude!.Value)))
            .ToArray();

        var nearestWithinRadius = candidates
            .Where(x => x.DistanceKm <= radiusKm)
            .OrderBy(x => x.DistanceKm)
            .FirstOrDefault();

        if (nearestWithinRadius is not null)
        {
            return nearestWithinRadius.Station;
        }

        var requestedCode = NormalizeOptionalText(requestedStationCode);
        if (requestedCode is null)
        {
            return null;
        }

        var requestedStation = candidates.FirstOrDefault(x =>
            StationCodeMatches(x.Station.StationCode, requestedCode));

        return requestedStation is not null && requestedStation.DistanceKm <= radiusKm
            ? requestedStation.Station
            : null;
    }

    private static bool StationCodeMatches(string stationCode, string requestedStationCode)
    {
        var normalizedStationCode = NormalizeStationCode(stationCode);
        var normalizedRequestedCode = NormalizeStationCode(requestedStationCode);
        return string.Equals(normalizedStationCode, normalizedRequestedCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(WithoutStationPrefix(normalizedStationCode), WithoutStationPrefix(normalizedRequestedCode), StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeStationCode(string value) => value.Trim().ToUpperInvariant();

    private static string WithoutStationPrefix(string value) =>
        value.StartsWith("ST-", StringComparison.OrdinalIgnoreCase)
            ? value[3..]
            : value;

    private static double CalculateDistanceKm(
        decimal startLat,
        decimal startLng,
        decimal endLat,
        decimal endLng)
    {
        const double earthRadiusKm = 6371d;
        var dLat = ToRadians(endLat - startLat);
        var dLng = ToRadians(endLng - startLng);
        var lat1 = ToRadians(startLat);
        var lat2 = ToRadians(endLat);

        var a = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d)
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLng / 2d) * Math.Sin(dLng / 2d);
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return earthRadiusKm * c;
    }

    private static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180d;

    private sealed record StationCandidate(Station Station, double DistanceKm);
}
