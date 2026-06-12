using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class CustomBookingRequest : BaseGuidAuditableEntity
{
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    public Guid? ContactUserId { get; set; }

    public User? ContactUser { get; set; }

    public string ContactName { get; set; } = null!;

    public string ContactPhone { get; set; } = null!;

    public string? ContactEmail { get; set; }

    public Guid? PreferredVesselId { get; set; }

    public Vessel? PreferredVessel { get; set; }

    public DateOnly DepartureDate { get; set; }

    public TimeOnly? PreferredStartTime { get; set; }

    public TimeOnly? PreferredEndTime { get; set; }

    public DateOnly? EstimatedEndDate { get; set; }

    public int EstimatedTravelMinutes { get; set; }

    public int EstimatedStayMinutes { get; set; }

    public int BufferMinutes { get; set; }

    public int EstimatedDurationMinutes { get; set; }

    public string FromLocation { get; set; } = null!;

    public string ToLocation { get; set; } = null!;

    public Guid? FromStationId { get; set; }

    public string? FromStationCode { get; set; }

    public Station? FromStation { get; set; }

    public Guid? ToStationId { get; set; }

    public string? ToStationCode { get; set; }

    public Station? ToStation { get; set; }

    public string? ItineraryNote { get; set; }

    public int PassengerCount { get; set; }

    public int AdultCount { get; set; }

    public int ChildCount { get; set; }

    public string? SpecialRequests { get; set; }

    public CustomBookingRequestStatus Status { get; set; } = CustomBookingRequestStatus.PendingReview;

    public DateTimeOffset? QuotedAt { get; set; }

    public Guid? QuotedByUserId { get; set; }

    public User? QuotedByUser { get; set; }

    public DateTimeOffset? QuoteAcceptedAt { get; set; }

    public CustomBookingQuote? Quote { get; set; }

    public ICollection<CustomBookingItineraryStop> ItineraryStops { get; set; } = new List<CustomBookingItineraryStop>();
}
