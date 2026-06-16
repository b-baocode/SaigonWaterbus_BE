namespace SaigonWaterbus.Domain.Entities;

public class CustomBookingOperationService : BaseGuidAuditableEntity
{
    public Guid CustomBookingRequestId { get; set; }

    public CustomBookingRequest CustomBookingRequest { get; set; } = null!;

    public string ServiceName { get; set; } = null!;

    public int Quantity { get; set; }

    public string? Note { get; set; }
}
