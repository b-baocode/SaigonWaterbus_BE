namespace SaigonWaterbus.Domain.Constants;

public static class RouteTypes
{
    public const string Regular = "Regular";
    public const string SightseeingLoop = "SightseeingLoop";
    public const string CharterReference = "CharterReference";

    public static bool IsValid(string? value) =>
        string.Equals(value, Regular, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, SightseeingLoop, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, CharterReference, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? value) =>
        value?.Trim() switch
        {
            var routeType when string.Equals(routeType, SightseeingLoop, StringComparison.OrdinalIgnoreCase) => SightseeingLoop,
            var routeType when string.Equals(routeType, CharterReference, StringComparison.OrdinalIgnoreCase) => CharterReference,
            _ => Regular
        };

    public static bool IsBookableByDefault(string routeType) =>
        !string.Equals(routeType, CharterReference, StringComparison.Ordinal);
}
