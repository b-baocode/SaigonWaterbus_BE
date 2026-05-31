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
