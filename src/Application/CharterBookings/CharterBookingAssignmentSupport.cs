using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
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

    public static CharterBookingUserAssignmentDto? ToUserAssignmentDto(User? user) =>
        user is null ? null : new CharterBookingUserAssignmentDto(user.Id, user.FullName, user.UserCode);

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

        var workingDate = booking.DepartureDate.Value;
        return await context.StaffWorkAssignments.AnyAsync(
            assignment => assignment.StaffUserId == staffUserId
                && assignment.Status != StaffWorkAssignmentStatus.Cancelled
                && assignment.AssignmentType == StaffWorkAssignmentType.Boat
                && assignment.BoatId.HasValue
                && selectedBoatIds.Contains(assignment.BoatId.Value)
                && assignment.WorkingDate == workingDate,
            cancellationToken);
    }
}
