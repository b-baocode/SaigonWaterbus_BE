namespace SaigonWaterbus.Application.Fares;

public sealed record FareByTicketTypeDto(
    Guid TicketTypeId,
    string TicketTypeCode,
    string TicketTypeName,
    decimal BasePrice,
    decimal PriceModifier,
    decimal FinalPrice);
