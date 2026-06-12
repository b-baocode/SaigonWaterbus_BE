namespace SaigonWaterbus.Domain.Entities;

public class CustomBookingItineraryStop : BaseGuidAuditableEntity
{
    public Guid CustomBookingRequestId { get; set; }

    public CustomBookingRequest CustomBookingRequest { get; set; } = null!;

    public int StopOrder { get; set; }

    public Guid StationId { get; set; }

    public Station Station { get; set; } = null!;

    public int StayDurationMinutes { get; set; }

    public string? Note { get; set; }
}
