using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class BookingTicket : BaseGuidAuditableEntity
{
    public Guid BookingId { get; set; }
    public Guid? BookingPassengerId { get; set; }
    public string TicketCode { get; set; } = null!;
    public string QrToken { get; set; } = null!;
    public string TicketTypeCode { get; set; } = null!;
    public string TicketTypeName { get; set; } = null!;
    public BookingTicketStatus TicketStatus { get; set; } = BookingTicketStatus.Active;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? CheckedInAt { get; set; }
    public Guid? CheckedInByUserId { get; set; }

    public Booking Booking { get; set; } = null!;
    public BookingPassenger? BookingPassenger { get; set; }
    public User? CheckedInByUser { get; set; }
}
