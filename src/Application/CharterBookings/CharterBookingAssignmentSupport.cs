using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.BoatStaffAssignments;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingAssignmentSupport
{
    public static IReadOnlyList<Guid> ResolveSelectedBoatIds(Booking booking)
    {
        var boatIds = booking.CharterBoats
            .OrderBy(x => x.BoatOrder)
            .Select(x => x.BoatId)
            .Distinct()
            .ToList();

        if (boatIds.Count == 0 && booking.BoatId.HasValue)
        {
            boatIds.Add(booking.BoatId.Value);
        }

        return boatIds;
    }

    public static async Task<User> EnsureCurrentUserIsAdminAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (AuthSupport.IsAdmin(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static async Task<User> EnsureAssignableManagerAsync(
        IApplicationDbContext context,
        Guid managerUserId,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var manager = await context.Users
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == managerUserId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy manager.");

        if (!string.Equals(manager.Role.SystemName, Roles.ManagerSystemName, StringComparison.Ordinal))
        {
            throw new ValidationException([new ValidationFailure(propertyName, "Người phụ trách phải có role Manager.")]);
        }

        if (manager.Status != UserStatus.Active)
        {
            throw new ValidationException([new ValidationFailure(propertyName, "Manager phải đang Active để được phân công.")]);
        }

        return manager;
    }

    public static void EnsureCanManageCharterStaff(
        User actor,
        Booking booking)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        if (AuthSupport.IsManager(actor) && booking.AssignedManagerId == actor.Id)
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static async Task EnsureCanViewOperationalAsync(
        IApplicationDbContext context,
        User actor,
        Booking booking,
        bool includeCustomerOwner,
        bool notFoundWhenDenied,
        CancellationToken cancellationToken)
    {
        if (includeCustomerOwner && booking.UserId == actor.Id)
        {
            return;
        }

        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        if (AuthSupport.IsManager(actor) && booking.AssignedManagerId == actor.Id)
        {
            return;
        }

        if (AuthSupport.IsStaff(actor)
            && await IsStaffAssignedToBookingAsync(context, actor.Id, booking, cancellationToken))
        {
            return;
        }

        if (notFoundWhenDenied)
        {
            throw new NotFoundException("Charter booking not found.");
        }

        throw new ForbiddenAccessException();
    }

    public static async Task<IReadOnlyList<CharterBookingStaffAssignmentDto>> LoadStaffAssignmentsAsync(
        IApplicationDbContext context,
        Booking booking,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        var selectedBoatIds = ResolveSelectedBoatIds(booking);
        if (selectedBoatIds.Count == 0 || !booking.DepartureDate.HasValue)
        {
            return [];
        }

        var query = context.BoatStaffAssignments
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.StaffUser)
            .Include(x => x.AssignedByUser)
            .Include(x => x.ReplacedByUser)
            .Where(x => selectedBoatIds.Contains(x.BoatId)
                && x.WorkingDate == booking.DepartureDate.Value);

        if (activeOnly)
        {
            query = query.Where(x => x.IsActive);
        }

        var assignments = await query
            .OrderBy(x => x.Boat.Name)
            .ThenBy(x => x.ShiftCode)
            .ThenBy(x => x.StaffUser.FullName)
            .ToListAsync(cancellationToken);

        return assignments.Select(ToStaffAssignmentDto).ToList();
    }

    public static CharterBookingUserAssignmentDto? ToUserAssignmentDto(User? user) =>
        user is null ? null : new CharterBookingUserAssignmentDto(user.Id, user.FullName, user.UserCode);

    public static CharterBookingStaffAssignmentDto ToStaffAssignmentDto(BoatStaffAssignment assignment) =>
        new(
            assignment.Id,
            assignment.BoatId,
            assignment.Boat.Name,
            assignment.StaffUserId,
            assignment.StaffUser.FullName,
            assignment.WorkingDate,
            assignment.ShiftCode ?? BoatStaffAssignmentSupport.DefaultShiftCode,
            BoatStaffAssignmentSupport.OnBoardDutyRole,
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

    public static async Task<bool> IsStaffAssignedToBookingAsync(
        IApplicationDbContext context,
        Guid staffUserId,
        Booking booking,
        CancellationToken cancellationToken)
    {
        var selectedBoatIds = ResolveSelectedBoatIds(booking);
        if (selectedBoatIds.Count == 0 || !booking.DepartureDate.HasValue)
        {
            return false;
        }

        return await context.BoatStaffAssignments.AnyAsync(
            x => x.StaffUserId == staffUserId
                && selectedBoatIds.Contains(x.BoatId)
                && x.WorkingDate == booking.DepartureDate.Value
                && x.IsActive,
            cancellationToken);
    }
}
