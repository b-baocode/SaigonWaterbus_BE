using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

internal static class TicketStaffScanAuthorizationSupport
{
    public static async Task EnsureStaffCanOperateTicketAsync(
        IApplicationDbContext context,
        User actor,
        Ticket ticket,
        DateTimeOffset serverTime,
        CancellationToken cancellationToken)
    {
        if (!AuthSupport.IsStaff(actor))
        {
            return;
        }

        if (Booking.IsCharterBookingType(ticket.Booking.BookingType))
        {
            await EnsureStaffCanOperateCharterBookingAsync(
                context, actor, ticket.Booking, serverTime, cancellationToken);
            return;
        }

        var passengerTripId = ticket.BookingPassenger?.TripId;
        var trip = ticket.BookingPassenger?.Trip
            ?? (passengerTripId.HasValue && passengerTripId == ticket.Booking.ReturnTripId
                ? ticket.Booking.ReturnTrip
                : ticket.Booking.Trip);
        await EnsureStaffCanOperateTripAsync(context, actor, trip, serverTime, cancellationToken);
    }

    public static async Task EnsureStaffCanOperateTripAsync(
        IApplicationDbContext context,
        User actor,
        Trip? trip,
        DateTimeOffset serverTime,
        CancellationToken cancellationToken)
    {
        if (!AuthSupport.IsStaff(actor))
        {
            return;
        }

        EnsureOnBoardStaff(actor);
        if (trip?.BoatId is not Guid boatId)
        {
            throw new ValidationException([new ValidationFailure("trip",
                "Chuyến chưa gắn tàu nên nhân viên không thể scan/check vé.")]);
        }

        var assignments = await LoadUsableBoatAssignmentsAsync(
            context, actor.Id, [boatId], cancellationToken);
        if (!assignments.Any(x => CanOperateTrip(x, trip, serverTime)))
        {
            throw MissingActiveAssignment("tàu của chuyến");
        }
    }

    public static async Task EnsureStaffCanOperateAnyTripAsync(
        IApplicationDbContext context,
        User actor,
        IEnumerable<Trip?> trips,
        DateTimeOffset serverTime,
        CancellationToken cancellationToken)
    {
        if (!AuthSupport.IsStaff(actor))
        {
            return;
        }

        EnsureOnBoardStaff(actor);
        var candidates = trips
            .Where(x => x?.BoatId.HasValue == true)
            .Select(x => x!)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();
        if (candidates.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("trip",
                "Booking chưa gắn chuyến/tàu nên nhân viên không thể xem danh sách khách.")]);
        }

        var boatIds = candidates.Select(x => x.BoatId!.Value).Distinct().ToArray();
        var assignments = await LoadUsableBoatAssignmentsAsync(
            context, actor.Id, boatIds, cancellationToken);
        if (!candidates.Any(trip => assignments.Any(x => CanOperateTrip(x, trip, serverTime))))
        {
            throw MissingActiveAssignment("ít nhất một tàu của booking");
        }
    }

    public static async Task EnsureStaffCanOperateCharterBookingAsync(
        IApplicationDbContext context,
        User actor,
        Booking booking,
        DateTimeOffset serverTime,
        CancellationToken cancellationToken)
    {
        if (!AuthSupport.IsStaff(actor))
        {
            return;
        }

        EnsureOnBoardStaff(actor);
        var boatIds = await context.CharterBookingBoats
            .AsNoTracking()
            .Where(x => x.BookingId == booking.Id)
            .Select(x => x.BoatId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (boatIds.Count == 0 && booking.BoatId.HasValue)
        {
            boatIds.Add(booking.BoatId.Value);
        }

        if (boatIds.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("booking",
                "Booking thuê tàu chưa được chọn tàu nên nhân viên không thể scan/check vé.")]);
        }

        var assignments = await LoadUsableBoatAssignmentsAsync(
            context, actor.Id, boatIds, cancellationToken);
        if (!assignments.Any(x => x.StartAt <= serverTime && x.EndAt >= serverTime))
        {
            throw MissingActiveAssignment("tàu được chọn cho booking thuê tàu");
        }
    }

    private static Task<List<StaffWorkAssignment>> LoadUsableBoatAssignmentsAsync(
        IApplicationDbContext context,
        Guid staffUserId,
        IReadOnlyCollection<Guid> boatIds,
        CancellationToken cancellationToken) =>
        context.StaffWorkAssignments
            .AsNoTracking()
            .Where(x => x.StaffUserId == staffUserId
                && x.AssignmentType == StaffWorkAssignmentType.Boat
                && x.BoatId.HasValue
                && boatIds.Contains(x.BoatId.Value)
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced)
            .ToListAsync(cancellationToken);

    private static void EnsureOnBoardStaff(User actor)
    {
        if (actor.StaffType != StaffType.OnBoard)
        {
            throw new ValidationException([new ValidationFailure("staffWorkAssignment",
                "Chỉ nhân viên OnBoard trên tàu mới được scan/check vé.")]);
        }
    }

    private static bool CanOperateTrip(
        StaffWorkAssignment assignment,
        Trip trip,
        DateTimeOffset serverTime)
    {
        if (assignment.BoatId != trip.BoatId)
        {
            return false;
        }

        if (assignment.StartAt <= serverTime && assignment.EndAt >= serverTime)
        {
            return true;
        }

        return assignment.StartAt <= trip.DepartureTime
            && assignment.EndAt >= trip.ArrivalTime
            && IsWithinRecordedDelayedOperationalWindow(trip, serverTime);
    }

    private static bool IsWithinRecordedDelayedOperationalWindow(
        Trip trip,
        DateTimeOffset serverTime)
    {
        var hasRecordedDelay = trip.DelayMinutes > 0
            || trip.AdjustedDepartureTime.HasValue
            || trip.AdjustedArrivalTime.HasValue
            || trip.TripStops.Any(x => x.AdjustedArrivalTime.HasValue
                || x.AdjustedDepartureTime.HasValue
                || (x.ActualArrivalTime.HasValue
                    && x.PlannedArrivalTime.HasValue
                    && x.ActualArrivalTime > x.PlannedArrivalTime)
                || (x.ActualDepartureTime.HasValue
                    && x.PlannedDepartureTime.HasValue
                    && x.ActualDepartureTime > x.PlannedDepartureTime));
        if (!hasRecordedDelay || serverTime < trip.DepartureTime)
        {
            return false;
        }

        var operationalEnd = TripDelaySupport.ResolveAdjustedArrival(trip);
        foreach (var stop in trip.TripStops)
        {
            var arrival = stop.ActualArrivalTime
                ?? stop.AdjustedArrivalTime
                ?? stop.PlannedArrivalTime;
            if (arrival.HasValue)
            {
                var dwellMinutes = stop.StayDurationMinutes > 0
                    ? stop.StayDurationMinutes
                    : TicketAttendanceWindowSupport.UnscheduledDwellFallbackMinutes;
                operationalEnd = Max(operationalEnd, arrival.Value.AddMinutes(dwellMinutes));
            }

            var departure = stop.ActualDepartureTime
                ?? stop.AdjustedDepartureTime
                ?? stop.PlannedDepartureTime;
            if (departure.HasValue)
            {
                operationalEnd = Max(operationalEnd, departure.Value);
            }
        }

        return serverTime <= operationalEnd.AddMinutes(TicketAttendanceWindowSupport.CheckOutGraceMinutes);
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    private static ValidationException MissingActiveAssignment(string target) =>
        new([new ValidationFailure("staffWorkAssignment",
            $"Nhân viên này chưa có ca OnBoard phù hợp đang active trên {target}.")]);
}
