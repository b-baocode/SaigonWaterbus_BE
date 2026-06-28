using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Ticket : BaseGuidAuditableEntity
{
    public Guid BookingId { get; set; }
    public Guid? BookingPassengerId { get; set; }
    public string TicketCode { get; set; } = null!;
    public string QrToken { get; set; } = null!;
    public string TicketTypeCode { get; set; } = null!;
    public string TicketTypeName { get; set; } = null!;
    public TicketStatus TicketStatus { get; set; } = TicketStatus.Active;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? CheckedInAt { get; set; }
    public Guid? CheckedInByUserId { get; set; }
    public DateTimeOffset? CheckedOutAt { get; set; }
    public Guid? CheckedOutByUserId { get; set; }
    public Guid? ReissuedFromTicketId { get; set; }
    public string? ReissueReason { get; set; }
    public DateTimeOffset? ReissuedAt { get; set; }
    public Guid? ReissuedByUserId { get; set; }

    public Booking Booking { get; set; } = null!;
    public BookingPassenger? BookingPassenger { get; set; }
    public User? CheckedInByUser { get; set; }
    public User? CheckedOutByUser { get; set; }
    public User? ReissuedByUser { get; set; }
}
