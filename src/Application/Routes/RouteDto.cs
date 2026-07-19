namespace SaigonWaterbus.Application.Routes;

public sealed record RouteDto(
    Guid RouteId,
    string RouteCode,
    string RouteName,
    string RouteType,
    string? Description,
    decimal? BaseDistanceKm,
    decimal? EstimatedDurationMin,
    string Status,
    string RouteLabel = "",
    bool IsSelectableForCharterQuote = false,
    bool IsGeneratedForBooking = false);

public sealed record RouteDetailDto(
    Guid RouteId,
    string RouteCode,
    string RouteName,
    string RouteType,
    string? Description,
    decimal? BaseDistanceKm,
    decimal? EstimatedDurationMin,
    string Status,
    IReadOnlyList<RouteStopDto> Stops,
    IReadOnlyList<double[]>? RouteGeometry,
    string RouteLabel = "",
    bool IsSelectableForCharterQuote = false,
    bool IsGeneratedForBooking = false);

public sealed record RouteStopDto(
    Guid RouteStopId,
    Guid StationId,
    string StationCode,
    string StationName,
    int StopOrder,
    decimal? StandardTravelMin,
    decimal? DistanceFromPreviousKm,
    bool IsPickupAllowed,
    bool IsDropoffAllowed);
