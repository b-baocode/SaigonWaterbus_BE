namespace SaigonWaterbus.Application.Routes;

public sealed record RouteDto(
    Guid RouteId,
    string RouteCode,
    string RouteName,
    string RouteType,
    string? Description,
    decimal? BaseDistanceKm,
    int? EstimatedDurationMin,
    string Status);

public sealed record RouteDetailDto(
    Guid RouteId,
    string RouteCode,
    string RouteName,
    string RouteType,
    string? Description,
    decimal? BaseDistanceKm,
    int? EstimatedDurationMin,
    string Status,
    IReadOnlyList<RouteStopDto> Stops,
    IReadOnlyList<double[]>? RouteGeometry);

public sealed record RouteStopDto(
    Guid RouteStopId,
    Guid StationId,
    string StationCode,
    string StationName,
    int StopOrder,
    int? StandardTravelMin,
    bool IsPickupAllowed,
    bool IsDropoffAllowed);
