namespace SaigonWaterbus.Application.TicketTypes;

public sealed record TicketTypeDto(
    string TicketTypeCode,
    string TicketTypeName,
    string? Description,
    decimal PriceModifier,
    IReadOnlyList<string>? AllowedSeatTypeCodes,
    decimal? SightseeingPriceModifier = null);
