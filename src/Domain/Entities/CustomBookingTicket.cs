using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class CustomBookingTicket : BaseGuidAuditableEntity
{
    public Guid CustomBookingRequestId { get; set; }

    public CustomBookingRequest CustomBookingRequest { get; set; } = null!;

    public string TicketCode { get; set; } = null!;

    public string QrTokenHash { get; set; } = null!;

    public DateTimeOffset QrIssuedAt { get; set; }

    public DateTimeOffset? QrExpiresAt { get; set; }

    public DateTimeOffset? QrUsedAt { get; set; }

    public Guid? QrUsedByUserId { get; set; }

    public User? QrUsedByUser { get; set; }

    public CustomBookingTicketStatus Status { get; set; } = CustomBookingTicketStatus.Active;
}
