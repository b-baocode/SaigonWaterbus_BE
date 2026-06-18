using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.TripSchedules;

public sealed record TripScheduleDto(
    Guid Id,
    Guid RouteId,
    string RouteName,
    string RouteCode,
    Guid VesselId,
    string VesselName,
    TimeOnly DepartureTime,
    RecurrenceType RecurrenceType,
    string? WeeklyDays,
    int? MonthlyDay,
    int HorizonDays,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    bool IsActive);
