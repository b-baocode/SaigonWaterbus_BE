namespace SaigonWaterbus.Domain.Entities;

public class CustomBookingItineraryStop : BaseGuidAuditableEntity
{
    public Guid CustomBookingId { get; set; }

    public Guid StationId { get; set; }

    public int StopOrder { get; set; }

    public int StayDurationMinutes { get; set; }

    public string? Note { get; set; }

    public CustomBooking CustomBooking { get; set; } = null!;

    public Station Station { get; set; } = null!;
}
