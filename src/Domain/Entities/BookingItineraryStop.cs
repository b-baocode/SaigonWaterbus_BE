namespace SaigonWaterbus.Domain.Entities;

public class BookingItineraryStop : BaseGuidAuditableEntity
{
    public Guid BookingId { get; set; }

    public Guid StationId { get; set; }

    public int StopOrder { get; set; }

    public int StayDurationMinutes { get; set; }

    public string? Note { get; set; }

    public Booking Booking { get; set; } = null!;

    public Station Station { get; set; } = null!;
}
