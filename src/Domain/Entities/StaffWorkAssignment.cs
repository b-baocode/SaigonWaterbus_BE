using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class StaffWorkAssignment : BaseAuditableEntity
{
    public Guid StaffUserId { get; set; }

    public StaffWorkAssignmentType AssignmentType { get; set; }

    public Guid? BoatId { get; set; }

    public Guid? StationId { get; set; }

    public DateOnly WorkingDate { get; set; }

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public string? DutyRole { get; set; }

    public StaffWorkAssignmentStatus Status { get; set; } = StaffWorkAssignmentStatus.Scheduled;

    public Guid AssignedByUserId { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public string? Note { get; set; }

    public User StaffUser { get; set; } = null!;

    public User AssignedByUser { get; set; } = null!;

    public Boat? Boat { get; set; }

    public Station? Station { get; set; }
}
