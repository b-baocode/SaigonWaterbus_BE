using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Promotion : BaseGuidAuditableEntity
{
    public string PromotionCode { get; set; } = null!;
    public string PromotionName { get; set; } = null!;
    public string PromotionType { get; set; } = null!;
    public decimal DiscountValue { get; set; }
    public decimal? MinOrderValue { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }
    public int? UsageLimit { get; set; }
    public int UsageCount { get; set; } = 0;
    public string Status { get; set; } = "Active";

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
