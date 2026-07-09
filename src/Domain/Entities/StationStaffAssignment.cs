using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class StationStaffAssignment : BaseAuditableEntity
{
    public Guid StationId { get; set; }
    public Guid StaffUserId { get; set; }
    public OperationScheduleSourceType SourceType { get; set; }
    public Guid SourceId { get; set; }
    public DateOnly WorkingDate { get; set; }
    public string? ShiftCode { get; set; }
    public string? DutyRole { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }

    public Station Station { get; set; } = null!;
    public User StaffUser { get; set; } = null!;
    public User AssignedByUser { get; set; } = null!;
}
