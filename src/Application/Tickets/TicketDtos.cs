using System.Text.Json.Serialization;

namespace SaigonWaterbus.Application.Tickets;

public sealed record TicketScanDto(
    Guid TicketId,
    string TicketCode,
    string QrToken,
    string TicketTypeCode,
    string TicketTypeName,
    string TicketStatus,
    DateTimeOffset IssuedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? CheckedInAt,
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
    string? VesselName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FromStationName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToStationName,
    IReadOnlyList<TicketScanPassengerDto> Passengers);

public sealed record TicketScanPassengerDto(
    string FullName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? DateOfBirth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PassengerType);
