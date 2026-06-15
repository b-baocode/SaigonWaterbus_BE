namespace SaigonWaterbus.Application.Routes;

public sealed record RouteDto(
    Guid RouteId,
    string RouteCode,
    string RouteName,
    string? Description,
    decimal? BaseDistanceKm,
    int? EstimatedDurationMin,
    string Status);

public sealed record RouteDetailDto(
    Guid RouteId,
    string RouteCode,
    string RouteName,
    string? Description,
    decimal? BaseDistanceKm,
    int? EstimatedDurationMin,
    string Status,
    IReadOnlyList<RouteStopDto> Stops,
    IReadOnlyList<RouteSegmentDto> Segments,
    IReadOnlyList<double[]>? RouteGeometry);

public sealed record RouteStopDto(
    Guid RouteStopId,
    Guid StationId,
    string StationCode,
    string StationName,
    int StopOrder,
    int? StandardTravelMin,
    int? StandardDwellMin,
    bool IsPickupAllowed,
    bool IsDropoffAllowed);

public sealed record RouteSegmentDto(
    Guid RouteSegmentId,
    Guid RouteId,
    int SegmentOrder,
    Guid FromStationId,
    string FromStationCode,
    string FromStationName,
    Guid ToStationId,
    string ToStationCode,
    string ToStationName,
    decimal DistanceKm,
    int EstimatedTravelMinutes,
    IReadOnlyList<double[]>? Geometry);

public sealed record RouteSegmentCoordinateDto(
    double Longitude,
    double Latitude);
