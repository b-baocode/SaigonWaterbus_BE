using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class TicketScanEvent : BaseGuidAuditableEntity
{
    public Guid? TicketId { get; set; }

    public Guid? BookingId { get; set; }

    public Guid? TripId { get; set; }

    public Guid PerformedByUserId { get; set; }

    public Guid? StaffWorkAssignmentId { get; set; }

    public Guid? BoatId { get; set; }

    public Guid? StationId { get; set; }

    public Guid? TripStopId { get; set; }

    public TicketScanAction Action { get; set; }

    public TicketScanResult Result { get; set; }

    public string? FailureReason { get; set; }

    public TicketScanSource Source { get; set; } = TicketScanSource.Qr;

    public string? ClientOperationId { get; set; }

    public DateTimeOffset? DeviceTime { get; set; }

    public DateTimeOffset ServerTime { get; set; }

    public string? Note { get; set; }

    public string? ScannedCodeOrToken { get; set; }

    public TicketStatus? TicketStatusBefore { get; set; }

    public TicketStatus? TicketStatusAfter { get; set; }

    public Ticket? Ticket { get; set; }

    public Booking? Booking { get; set; }

    public Trip? Trip { get; set; }

    public User PerformedByUser { get; set; } = null!;

    public StaffWorkAssignment? StaffWorkAssignment { get; set; }

    public Boat? Boat { get; set; }

    public Station? Station { get; set; }

    public TripStop? TripStop { get; set; }
}
