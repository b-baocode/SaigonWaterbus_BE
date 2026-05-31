using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class TicketType : BaseGuidAuditableEntity
{
    public string TicketTypeCode { get; set; } = null!;
    public string TicketTypeName { get; set; } = null!;
    public string? Description { get; set; }
    public decimal PriceModifier { get; set; } = 1.0m;
    public int PointsEarnedRate { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    public ICollection<BookingItem> BookingItems { get; set; } = new List<BookingItem>();
}
