using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class SeatType : BaseGuidAuditableEntity
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public decimal BasePrice { get; set; }
    public string Currency { get; set; } = "VND";
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
}
