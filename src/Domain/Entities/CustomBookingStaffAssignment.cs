namespace SaigonWaterbus.Domain.Entities;

public class CustomBookingStaffAssignment : BaseGuidAuditableEntity
{
    public Guid CustomBookingRequestId { get; set; }

    public CustomBookingRequest CustomBookingRequest { get; set; } = null!;

    public Guid StaffUserId { get; set; }

    public User StaffUser { get; set; } = null!;

    public string? DutyNote { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public Guid AssignedByManagerUserId { get; set; }

    public User AssignedByManagerUser { get; set; } = null!;
}
