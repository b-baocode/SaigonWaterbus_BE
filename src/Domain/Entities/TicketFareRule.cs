using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class TicketFareRule : BaseGuidAuditableEntity
{
    public string TicketTypeCode { get; set; } = null!;

    public string RouteType { get; set; } = null!;

    public decimal PriceModifier { get; set; }

    public bool IsActive { get; set; } = true;
}
