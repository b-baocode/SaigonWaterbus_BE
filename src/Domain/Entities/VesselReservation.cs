using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class VesselReservation : BaseGuidAuditableEntity
{
    public Guid VesselId { get; set; }

    public Vessel Vessel { get; set; } = null!;

    public VesselReservationSourceType SourceType { get; set; }

    public Guid SourceId { get; set; }

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public VesselReservationStatus Status { get; set; } = VesselReservationStatus.Held;

    public DateTimeOffset? ExpiresAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? ConfirmedByUserId { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset? ReleasedAt { get; set; }

    public string? ReleaseReason { get; set; }
}
