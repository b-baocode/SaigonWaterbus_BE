using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

internal static class TicketStaffScanAuthorizationSupport
{
    public static Task EnsureStaffCanOperateTicketAsync(
        IApplicationDbContext context,
        User actor,
        Ticket ticket,
        DateTimeOffset serverTime,
        CancellationToken cancellationToken)
    {
        var boatId = ticket.BookingPassenger?.Trip?.BoatId
            ?? ticket.Booking.Trip?.BoatId
            ?? ticket.Booking.BoatId;
        return EnsureStaffCanOperateBoatAsync(
            context,
            actor,
            boatId,
            serverTime,
            cancellationToken);
    }

    public static Task EnsureStaffCanOperateTripAsync(
        IApplicationDbContext context,
        User actor,
        Trip? trip,
        DateTimeOffset serverTime,
        CancellationToken cancellationToken) =>
        EnsureStaffCanOperateBoatAsync(
            context,
            actor,
            trip?.BoatId,
            serverTime,
            cancellationToken);

    private static async Task EnsureStaffCanOperateBoatAsync(
        IApplicationDbContext context,
        User actor,
        Guid? boatId,
        DateTimeOffset serverTime,
        CancellationToken cancellationToken)
    {
        if (!AuthSupport.IsStaff(actor))
        {
            return;
        }

        if (actor.StaffType != StaffType.OnBoard)
        {
            throw new ValidationException([new ValidationFailure("staffWorkAssignment",
                "Chỉ nhân viên OnBoard trên tàu mới được scan/check vé.")]);
        }

        if (!boatId.HasValue)
        {
            throw new ValidationException([new ValidationFailure("ticket",
                "Vé chưa gắn tàu nên nhân viên không thể scan/check.")]);
        }

        var hasActiveBoatAssignment = await context.StaffWorkAssignments
            .AsNoTracking()
            .AnyAsync(x => x.StaffUserId == actor.Id
                && x.AssignmentType == StaffWorkAssignmentType.Boat
                && x.BoatId == boatId.Value
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced
                && x.StartAt <= serverTime
                && x.EndAt >= serverTime,
                cancellationToken);

        if (!hasActiveBoatAssignment)
        {
            throw new ValidationException([new ValidationFailure("staffWorkAssignment",
                "Nhân viên này chưa có ca OnBoard đang active trên tàu của vé.")]);
        }
    }
}
