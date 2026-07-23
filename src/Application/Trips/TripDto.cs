using SaigonWaterbus.Application.Fares;

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
    string TripStatus,
    bool IsBookingClosed = false,
    bool IsBookable = true,
    string? BookingClosedReason = null,
    EffectiveFareAdjustmentDto? FareAdjustment = null);

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
    string? SourceBookingCode = null,
    TripBoatDto? Boat = null,
    IReadOnlyList<TripStaffAssignmentDto>? OnBoardStaff = null,
    int TotalPassengerCount = 0,
    string? RouteCode = null,
    TripRouteEndpointDto? FromStation = null,
    TripRouteEndpointDto? ToStation = null,
    int StopCount = 0);

public sealed record TripBoatDto(
    Guid VesselId,
    string VesselName,
    string VesselCode,
    int Capacity,
    string Status,
    string? ImageUrl = null,
    IReadOnlyList<string>? ImageUrls = null,
    string? RegistrationNumber = null,
    string? ServiceType = null,
    int? NumberOfDecks = null,
    int? MaxSpeedKmh = null,
    int? YearBuilt = null,
    string? Description = null);

public sealed record TripRouteEndpointDto(
    Guid StationId,
    string StationCode,
    string StationName,
    string? ImageUrl,
    IReadOnlyList<string> ImageUrls,
    string? Address = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    bool? HasWaitingArea = null,
    bool? HasParking = null,
    bool? HasTicketCounter = null);

public sealed record TripStaffAssignmentDto(
    Guid AssignmentId,
    Guid StaffUserId,
    string StaffName,
    string? StaffType,
    string AssignmentType,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Status,
    string ShiftState,
    string? DutyRole);

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
    string? Note = null,
    int BoardingPassengerCount = 0,
    IReadOnlyList<TripStaffAssignmentDto>? ScanningStaff = null,
    int AlightingPassengerCount = 0,
    int OnboardPassengerCount = 0,
    int SegmentPassengerCount = 0,
    string? StationImageUrl = null,
    IReadOnlyList<string>? StationImageUrls = null,
    string? StationAddress = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    bool? HasWaitingArea = null,
    bool? HasParking = null,
    bool? HasTicketCounter = null);

public sealed record TripStopPassengerCounts(
    int BoardingPassengerCount,
    int AlightingPassengerCount,
    int OnboardPassengerCount,
    int SegmentPassengerCount);
