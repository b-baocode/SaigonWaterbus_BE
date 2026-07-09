using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class BoatCrewAssignment : BaseAuditableEntity
{
    public Guid BoatId { get; set; }
    public Guid StaffUserId { get; set; }
    public CrewRole CrewRole { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public Guid? ReplacesAssignmentId { get; set; }
    public string? ReplacementReason { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }

    public Boat Boat { get; set; } = null!;
    public User StaffUser { get; set; } = null!;
    public User AssignedByUser { get; set; } = null!;
    public BoatCrewAssignment? ReplacesAssignment { get; set; }
    public ICollection<BoatCrewAssignment> ReplacementAssignments { get; set; } = new List<BoatCrewAssignment>();
}
