namespace SaigonWaterbus.Application.Tracking;

public static class TrackingEtaSupport
{
    public const double DockingThresholdKm = 0.08d;

    public sealed record EtaResult(decimal? RemainingDistanceKm, int? RemainingMinutes);

    public static EtaResult Resolve(
        decimal latitude,
        decimal longitude,
        decimal? stationLatitude,
        decimal? stationLongitude,
        decimal? speedKmh,
        DateTimeOffset? plannedArrivalAt,
        DateTimeOffset now,
        decimal? suppliedDistanceKm = null,
        int? suppliedMinutes = null)
    {
        var distanceKm = suppliedDistanceKm;
        if (!distanceKm.HasValue && stationLatitude.HasValue && stationLongitude.HasValue)
        {
            distanceKm = (decimal)CalculateDistanceKm(
                latitude,
                longitude,
                stationLatitude.Value,
                stationLongitude.Value);
            distanceKm = Math.Round(distanceKm.Value, 3, MidpointRounding.AwayFromZero);
        }

        if (suppliedMinutes.HasValue)
        {
            return new(distanceKm, suppliedMinutes.Value);
        }

        if (distanceKm.HasValue && distanceKm.Value <= (decimal)DockingThresholdKm)
        {
            return new(distanceKm, 0);
        }

        if (distanceKm is > 0m && speedKmh is > 0m)
        {
            var minutes = (int)Math.Ceiling((double)distanceKm.Value / (double)speedKmh.Value * 60d);
            return new(distanceKm, Math.Max(1, minutes));
        }

        if (plannedArrivalAt.HasValue && plannedArrivalAt.Value > now)
        {
            return new(distanceKm, Math.Max(1, (int)Math.Ceiling((plannedArrivalAt.Value - now).TotalMinutes)));
        }

        return new(distanceKm, null);
    }

    public static double CalculateDistanceKm(
        decimal startLatitude,
        decimal startLongitude,
        decimal endLatitude,
        decimal endLongitude)
    {
        const double earthRadiusKm = 6371d;
        var dLat = ToRadians(endLatitude - startLatitude);
        var dLng = ToRadians(endLongitude - startLongitude);
        var startLat = ToRadians(startLatitude);
        var endLat = ToRadians(endLatitude);
        var a = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d)
            + Math.Cos(startLat) * Math.Cos(endLat) * Math.Sin(dLng / 2d) * Math.Sin(dLng / 2d);
        var clampedA = Math.Clamp(a, 0d, 1d);
        return earthRadiusKm * 2d * Math.Atan2(Math.Sqrt(clampedA), Math.Sqrt(1d - clampedA));
    }

    private static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180d;
}
