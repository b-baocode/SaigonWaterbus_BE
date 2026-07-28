using System.Text.Json.Serialization;

namespace SaigonWaterbus.Application.Tickets;

public sealed record TicketScanDto(
    Guid TicketId,
    string TicketCode,
    string QrToken,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TicketTypeCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TicketTypeName,
    string TicketStatus,
    DateTimeOffset IssuedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? CheckedInAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? CheckedInByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CheckedInByName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? CheckedOutAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? CheckedOutByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CheckedOutByName,
    Guid BookingId,
    string BookingCode,
    string BookingType,
    string BookingStatus,
    string PaymentStatus,
    string ContactName,
    string ContactPhone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContactEmail,
    int PassengerCount,
    int RegisteredPassengerCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? AdultCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? ChildCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? DepartureDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    TimeOnly? StartTime,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TripCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? ScheduledDeparture,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? ScheduledArrival,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoatName,
    [property: JsonIgnore]
    string? VesselName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FromStationName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToStationName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SeatCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    TicketScanPassengerDto? TicketPassenger,
    IReadOnlyList<TicketScanPassengerDto> Passengers,
    Guid? FromStationId = null,
    Guid? ToStationId = null,
    Guid? BoatId = null,
    bool CanCheckIn = false,
    bool CanCheckOut = false);

public sealed record TicketScanPassengerDto(
    Guid PassengerId,
    string FullName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PhoneNumber,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Email,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? BirthYear,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PassengerType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SeatCode,
    bool IsLapInfant = false,
    Guid? CompanionPassengerId = null,
    string? CompanionPassengerName = null);
