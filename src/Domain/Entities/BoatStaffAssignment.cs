namespace SaigonWaterbus.Domain.Entities;

public class BoatStaffAssignment : BaseAuditableEntity
{
    public Guid BoatId { get; set; }
    public Guid StaffUserId { get; set; }
    public DateOnly WorkingDate { get; set; }
    public string? ShiftCode { get; set; }
    public string? DutyRole { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public Guid? ReplacesAssignmentId { get; set; }
    public Guid? ReplacedByAssignmentId { get; set; }
    public string? ReplacementReason { get; set; }
    public DateTimeOffset? ReplacedAt { get; set; }
    public Guid? ReplacedByUserId { get; set; }

    public Boat Boat { get; set; } = null!;
    public User StaffUser { get; set; } = null!;
    public User AssignedByUser { get; set; } = null!;
    public BoatStaffAssignment? ReplacesAssignment { get; set; }
    public BoatStaffAssignment? ReplacedByAssignment { get; set; }
    public User? ReplacedByUser { get; set; }
}
