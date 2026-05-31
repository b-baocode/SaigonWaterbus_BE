namespace SaigonWaterbus.Application.TicketTypes;

public sealed record TicketTypeDto(
    Guid TicketTypeId,
    string TicketTypeCode,
    string TicketTypeName,
    string? Description,
    decimal PriceModifier,
    int PointsEarnedRate,
    bool IsActive);
