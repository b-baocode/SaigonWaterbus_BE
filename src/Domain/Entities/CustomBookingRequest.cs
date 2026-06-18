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

    public Guid? WaterbusServiceId { get; set; }

    public WaterbusService? WaterbusService { get; set; }

    public int RequestedNumberOfDecks { get; set; }

    public SeatSetupType RequestedSeatSetupType { get; set; }

    public VesselRentalUnit RentalUnit { get; set; } = VesselRentalUnit.Day;

    public Guid? PreferredVesselId { get; set; }

    public Vessel? PreferredVessel { get; set; }

    public Guid? AssignedVesselId { get; set; }

    public Vessel? AssignedVessel { get; set; }

    public DateTimeOffset? AssignedAt { get; set; }

    public Guid? AssignedByUserId { get; set; }

    public Guid? AssignedManagerUserId { get; set; }

    public User? AssignedManagerUser { get; set; }

    public DateTimeOffset? ManagerAssignedAt { get; set; }

    public Guid? ManagerAssignedByUserId { get; set; }

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

    public PassengerManifestStatus PassengerManifestStatus { get; set; } = PassengerManifestStatus.NotStarted;

    public DateTimeOffset? PassengerManifestCompletedAt { get; set; }

    public Guid? PassengerManifestCompletedByUserId { get; set; }

    public User? PassengerManifestCompletedByUser { get; set; }

    public CustomBookingRequestStatus Status { get; set; } = CustomBookingRequestStatus.PendingReview;

    public string? StatusReason { get; set; }

    public DateTimeOffset? QuotedAt { get; set; }

    public Guid? QuotedByUserId { get; set; }

    public User? QuotedByUser { get; set; }

    public DateTimeOffset? QuoteAcceptedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledByUserId { get; set; }

    public CustomBookingQuote? Quote { get; set; }

    public ICollection<CustomBookingItineraryStop> ItineraryStops { get; set; } = new List<CustomBookingItineraryStop>();

    public ICollection<CustomBookingStaffAssignment> StaffAssignments { get; set; } =
        new List<CustomBookingStaffAssignment>();

    public ICollection<CustomBookingOperationService> OperationServices { get; set; } =
        new List<CustomBookingOperationService>();

    public ICollection<CustomBookingTicket> Tickets { get; set; } = new List<CustomBookingTicket>();

    public ICollection<CustomBookingPassenger> Passengers { get; set; } = new List<CustomBookingPassenger>();
}
