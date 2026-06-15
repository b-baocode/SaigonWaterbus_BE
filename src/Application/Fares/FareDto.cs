using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Fares;

public sealed record FareMatrixDto(
    Guid FareId,
    Guid RouteId,
    Guid FromStationId,
    string FromStationName,
    Guid ToStationId,
    string ToStationName,
    decimal BasePrice,
    bool IsActive);

public sealed record FareByTicketTypeDto(
    Guid TicketTypeId,
    string TicketTypeName,
    decimal BasePrice,
    decimal PriceModifier,
    decimal FinalPrice);

public sealed record FareByServiceSeatTypeDto(
    Guid TicketTypeId,
    string TicketTypeName,
    Guid ServiceId,
    string ServiceCode,
    Guid SeatTypeId,
    string SeatTypeCode,
    decimal BasePrice,
    decimal TicketTypeModifier,
    decimal SeatTypeModifier,
    decimal FinalPrice);

public sealed record VesselRentalFareDto(
    Guid VesselId,
    string VesselCode,
    string VesselName,
    int SeatCount,
    int PassengerCapacity,
    int NumberOfDecks,
    string ImageUrl,
    string? Description,
    VesselRentalUnit RentalUnit,
    decimal UnitPrice,
    string Currency,
    string? Note);
