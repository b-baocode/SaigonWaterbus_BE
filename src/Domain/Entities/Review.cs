using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Review : BaseGuidAuditableEntity
{
    public Guid CustomerId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? TripId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string Status { get; set; } = "Published";

    public User Customer { get; set; } = null!;
    public Booking? Booking { get; set; }
    public Trip? Trip { get; set; }
}
