namespace SaigonWaterbus.Application.Trips;

public sealed record TripSummaryDto(
    Guid TripId,
    string TripCode,
    string RouteName,
    string RouteType,
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
    // SellsBySegment: FE hỏi trạm lên/xuống khi true; false = đi nguyên chuyến (ngắm cảnh vòng lặp).
    string RouteType,
    bool SellsBySegment,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    int CapacitySnapshot,
    string TripStatus,
    string? StatusNote,
    IReadOnlyList<TripStopDto> Stops,
    string TripType = "Regular",
    Guid? SourceBookingId = null,
    string? SourceBookingCode = null);

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
    string StopStatus,
    int StayDurationMinutes = 0,
    string? Note = null);
