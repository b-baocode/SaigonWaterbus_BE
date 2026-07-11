using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Hubs;

[Authorize]
public sealed class CharterBookingsHub : Hub
{
    public const string ChangedEventName = "CharterBookingChanged";
    public const string AssignedListChangedEventName = "AssignedCharterBookingsChanged";
    public const string AssignedListGroupName = "charter-bookings:assigned";

    private readonly IApplicationDbContext _context;

    public CharterBookingsHub(IApplicationDbContext context)
    {
        _context = context;
    }

    public static string BookingGroupName(Guid bookingId) => $"charter-booking:{bookingId:N}";

    public async Task JoinAssignedCharterBookings()
    {
        var actor = await GetCurrentActiveUserAsync();
        if (!IsOperationalRole(actor))
        {
            throw new HubException("Forbidden.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, AssignedListGroupName, Context.ConnectionAborted);
    }

    public Task LeaveAssignedCharterBookings() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, AssignedListGroupName, Context.ConnectionAborted);

    public async Task JoinAdminCharterBookings()
    {
        var actor = await GetCurrentActiveUserAsync();
        if (!IsAdmin(actor))
        {
            throw new HubException("Forbidden.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, AssignedListGroupName, Context.ConnectionAborted);
    }

    public Task LeaveAdminCharterBookings() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, AssignedListGroupName, Context.ConnectionAborted);

    public async Task JoinCharterBooking(Guid bookingId)
    {
        var actor = await GetCurrentActiveUserAsync();
        var booking = await _context.Set<Booking>()
            .AsNoTracking()
            .Include(x => x.CharterBoats)
            .SingleOrDefaultAsync(
                x => x.Id == bookingId && x.BookingType == Booking.CharterBookingType,
                Context.ConnectionAborted);

        if (booking is null || !await CanViewBookingAsync(actor, booking))
        {
            throw new HubException("Forbidden.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, BookingGroupName(bookingId), Context.ConnectionAborted);
    }

    public Task LeaveCharterBooking(Guid bookingId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, BookingGroupName(bookingId), Context.ConnectionAborted);

    private async Task<User> GetCurrentActiveUserAsync()
    {
        var userIdValue = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            throw new HubException("Unauthorized.");
        }

        var user = await _context.Users
            .AsNoTracking()
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == userId, Context.ConnectionAborted);

        if (user is null || user.Status != UserStatus.Active)
        {
            throw new HubException("Unauthorized.");
        }

        return user;
    }

    private async Task<bool> CanViewBookingAsync(User actor, Booking booking)
    {
        if (booking.UserId == actor.Id || IsAdmin(actor))
        {
            return true;
        }

        if (IsManager(actor) && booking.AssignedManagerId == actor.Id)
        {
            return true;
        }

        if (!IsStaff(actor) || !booking.DepartureDate.HasValue)
        {
            return false;
        }

        var boatIds = booking.CharterBoats
            .Select(x => x.BoatId)
            .Distinct()
            .ToArray();
        if (boatIds.Length == 0 && booking.BoatId.HasValue)
        {
            boatIds = [booking.BoatId.Value];
        }

        var workingDate = booking.DepartureDate.Value;
        return boatIds.Length > 0
            && await _context.BoatCrewAssignments
                .AsNoTracking()
                .AnyAsync(
                    assignment => assignment.StaffUserId == actor.Id
                        && assignment.IsActive
                        && boatIds.Contains(assignment.BoatId)
                        && assignment.FromDate <= workingDate
                        && (!assignment.ToDate.HasValue || assignment.ToDate.Value >= workingDate)
                        && (assignment.ReplacesAssignmentId != null
                            || !_context.BoatCrewAssignments.Any(replacement =>
                                replacement.ReplacesAssignmentId == assignment.Id
                                && replacement.IsActive
                                && replacement.FromDate <= workingDate
                                && replacement.ToDate.HasValue
                                && replacement.ToDate.Value >= workingDate)),
                    Context.ConnectionAborted);
    }

    private static bool IsOperationalRole(User actor) =>
        IsAdmin(actor) || IsManager(actor) || IsStaff(actor);

    private static bool IsAdmin(User actor) =>
        string.Equals(actor.Role.SystemName, Roles.AdminName, StringComparison.Ordinal);

    private static bool IsManager(User actor) =>
        string.Equals(actor.Role.SystemName, Roles.ManagerSystemName, StringComparison.Ordinal);

    private static bool IsStaff(User actor) =>
        string.Equals(actor.Role.SystemName, Roles.StaffSystemName, StringComparison.Ordinal);
}
