using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Booking : BaseGuidAuditableEntity
{
    public Guid? UserId { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid? PromotionId { get; set; }
    public Guid? TripId { get; set; }
    public string BookingCode { get; set; } = null!;
    public string ContactName { get; set; } = null!;
    public string ContactPhone { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public BookingStatus BookingStatus { get; set; } = BookingStatus.PendingPayment;
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = "Unpaid";
    public decimal DepositAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public int PointsUsed { get; set; } = 0;
    public int PointsEarned { get; set; } = 0;

    public User? User { get; set; }
    public WaterbusService? Service { get; set; }
    public Promotion? Promotion { get; set; }
    public Trip? Trip { get; set; }
    public ICollection<BookingPassenger> Passengers { get; set; } = new List<BookingPassenger>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<BookingTicket> Tickets { get; set; } = new List<BookingTicket>();
}
