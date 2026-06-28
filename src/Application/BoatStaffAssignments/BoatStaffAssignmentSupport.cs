using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.BoatStaffAssignments;

internal static class BoatStaffAssignmentSupport
{
    public const string DefaultShiftCode = "Day";

    public static async Task<User> EnsureCurrentUserCanManageBoatStaffAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsManager(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static async Task<User> EnsureCurrentUserCanViewBoatStaffAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsManager(actor) || AuthSupport.IsStaff(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static string NormalizeShiftCode(string? shiftCode) =>
        string.IsNullOrWhiteSpace(shiftCode)
            ? DefaultShiftCode
            : shiftCode.Trim();

    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static async Task EnsureStaffCanBeAssignedAsync(
        IApplicationDbContext context,
        Guid staffUserId,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var staff = await context.Users
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == staffUserId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy staff.");

        if (!string.Equals(staff.Role.SystemName, Roles.StaffSystemName, StringComparison.Ordinal))
        {
            throw new ValidationException([new ValidationFailure(propertyName, "Người được phân công phải có role Staff.")]);
        }

        if (staff.Status != UserStatus.Active)
        {
            throw new ValidationException([new ValidationFailure(propertyName, "Staff phải đang Active để được phân công.")]);
        }
    }

    public static async Task EnsureStaffIsAvailableAsync(
        IApplicationDbContext context,
        Guid staffUserId,
        DateOnly workingDate,
        string shiftCode,
        Guid? excludingAssignmentId,
        CancellationToken cancellationToken)
    {
        var hasConflict = await context.BoatStaffAssignments.AnyAsync(
            x => x.StaffUserId == staffUserId
              && x.WorkingDate == workingDate
              && x.ShiftCode == shiftCode
              && x.IsActive
              && (!excludingAssignmentId.HasValue || x.Id != excludingAssignmentId.Value),
            cancellationToken);

        if (hasConflict)
        {
            throw new ValidationException([new ValidationFailure(
                "staffUserId",
                "Staff này đã được phân công cho tàu khác trong cùng ngày/ca.")]);
        }
    }

    public static BoatStaffAssignmentDto ToDto(BoatStaffAssignment assignment) =>
        new(
            assignment.Id,
            assignment.BoatId,
            assignment.Boat.Name,
            assignment.StaffUserId,
            assignment.StaffUser.FullName,
            assignment.WorkingDate,
            assignment.ShiftCode ?? DefaultShiftCode,
            assignment.DutyRole,
            assignment.IsActive,
            assignment.AssignedByUserId,
            assignment.AssignedByUser.FullName,
            assignment.AssignedAt,
            assignment.ReplacesAssignmentId,
            assignment.ReplacedByAssignmentId,
            assignment.ReplacementReason,
            assignment.ReplacedAt,
            assignment.ReplacedByUserId,
            assignment.ReplacedByUser?.FullName);
}
