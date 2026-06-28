namespace SaigonWaterbus.Application.BoatStaffAssignments;

public sealed record BoatStaffAssignmentDto(
    Guid AssignmentId,
    Guid BoatId,
    string BoatName,
    Guid StaffUserId,
    string StaffName,
    DateOnly WorkingDate,
    string ShiftCode,
    string? DutyRole,
    bool IsActive,
    Guid AssignedByUserId,
    string AssignedByName,
    DateTimeOffset AssignedAt,
    Guid? ReplacesAssignmentId,
    Guid? ReplacedByAssignmentId,
    string? ReplacementReason,
    DateTimeOffset? ReplacedAt,
    Guid? ReplacedByUserId,
    string? ReplacedByName);
