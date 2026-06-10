namespace SaigonWaterbus.Application.Trips;

public sealed record TripSummaryDto(
    Guid TripId,
    string TripCode,
    string RouteName,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    DateTimeOffset? FromStopScheduledDeparture,
    DateTimeOffset? ToStopScheduledArrival,
    int AvailableSeats,
    int TotalSeats,
    decimal? MinPrice,
    string TripStatus);

public sealed record TripDetailDto(
    Guid TripId,
    string TripCode,
    Guid RouteId,
    string RouteName,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    int CapacitySnapshot,
    string TripStatus,
    string? StatusNote,
    IReadOnlyList<TripStopDto> Stops);

public sealed record TripStopDto(
    Guid TripStopId,
    Guid StationId,
    string StationName,
    string StationCode,
    int StopOrder,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? ActualArrival,
    DateTimeOffset? ActualDeparture,
    string StopStatus);
