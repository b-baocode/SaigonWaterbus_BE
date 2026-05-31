using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Seat : BaseGuidAuditableEntity
{
    public Guid BoatId { get; set; }
    public string SeatNumber { get; set; } = null!;
    public string? SeatClass { get; set; }
    public int? SeatRow { get; set; }
    public int? SeatColumn { get; set; }
    public bool IsActive { get; set; } = true;

    public Boat Boat { get; set; } = null!;
    public ICollection<SeatHold> SeatHolds { get; set; } = new List<SeatHold>();
    public ICollection<BookingItem> BookingItems { get; set; } = new List<BookingItem>();
}
