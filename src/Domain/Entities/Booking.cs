using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Booking : BaseGuidEntity
{
    public Guid UserId { get; set; }
    public Guid? PromotionId { get; set; }
    public string BookingCode { get; set; } = null!;
    public DateTimeOffset BookedAt { get; set; }
    public BookingStatus BookingStatus { get; set; } = BookingStatus.PendingPayment;
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public int PointsUsed { get; set; } = 0;
    public int PointsEarned { get; set; } = 0;

    public User User { get; set; } = null!;
    public Promotion? Promotion { get; set; }
    public ICollection<BookingItem> Items { get; set; } = new List<BookingItem>();
}
