using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Review : BaseGuidAuditableEntity
{
    public Guid CustomerId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? TripId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    // Review mới luôn ở trạng thái Hidden, admin duyệt (chuyển Published) thì mới hiển thị công khai.
    public string Status { get; set; } = "Hidden";

    public User Customer { get; set; } = null!;
    public Booking? Booking { get; set; }
    public Trip? Trip { get; set; }
}
