using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.StaffWorkAssignments;

public static class OnBoardStaffTripSupport
{
    public const int RequiredOnBoardStaffCount = 2;

    public static async Task EnsureBoatHasRequiredOnBoardStaffAsync(
        IApplicationDbContext context,
        Guid boatId,
        DateTimeOffset departureTime,
        DateTimeOffset arrivalTime,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var count = await CountCoveringOnBoardStaffAsync(
            context, boatId, departureTime, arrivalTime, cancellationToken);
        if (count < RequiredOnBoardStaffCount)
        {
            throw new ValidationException([new ValidationFailure(propertyName,
                $"Tàu cần có ít nhất {RequiredOnBoardStaffCount} nhân viên OnBoard được phân ca phủ toàn bộ thời gian chuyến.")]);
        }
    }

    public static async Task<bool> HasRequiredOnBoardStaffAsync(
        IApplicationDbContext context,
        Guid boatId,
        DateTimeOffset departureTime,
        DateTimeOffset arrivalTime,
        CancellationToken cancellationToken)
    {
        var count = await CountCoveringOnBoardStaffAsync(
            context, boatId, departureTime, arrivalTime, cancellationToken);
        return count >= RequiredOnBoardStaffCount;
    }

    public static async Task EnsureAssignmentCanBeCancelledAsync(
        IApplicationDbContext context,
        StaffWorkAssignment assignment,
        string propertyName,
        CancellationToken cancellationToken)
    {
        if (assignment.AssignmentType != StaffWorkAssignmentType.Boat
            || !assignment.BoatId.HasValue
            || assignment.Status is StaffWorkAssignmentStatus.Cancelled or StaffWorkAssignmentStatus.Replaced)
        {
            return;
        }

        var affectedTrips = await context.Set<Trip>()
            .AsNoTracking()
            .Where(x => x.BoatId == assignment.BoatId.Value
                && x.TripStatus != TripStatus.Completed
                && x.TripStatus != TripStatus.Cancelled
                && assignment.StartAt <= x.DepartureTime
                && assignment.EndAt >= x.ArrivalTime)
            .OrderBy(x => x.DepartureTime)
            .ToListAsync(cancellationToken);
        if (affectedTrips.Count == 0)
        {
            return;
        }

        var windowStart = affectedTrips.Min(x => x.DepartureTime);
        var windowEnd = affectedTrips.Max(x => x.ArrivalTime);
        var remainingAssignments = await context.StaffWorkAssignments
            .AsNoTracking()
            .Where(x => x.Id != assignment.Id
                && x.AssignmentType == StaffWorkAssignmentType.Boat
                && x.BoatId == assignment.BoatId.Value
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced
                && x.StaffUser.StaffType == StaffType.OnBoard
                && x.StartAt < windowEnd
                && windowStart < x.EndAt)
            .Select(x => new { x.StaffUserId, x.StartAt, x.EndAt })
            .ToListAsync(cancellationToken);

        var uncoveredTrip = affectedTrips.FirstOrDefault(trip => remainingAssignments
            .Where(x => x.StartAt <= trip.DepartureTime && x.EndAt >= trip.ArrivalTime)
            .Select(x => x.StaffUserId)
            .Distinct()
            .Count() < RequiredOnBoardStaffCount);
        if (uncoveredTrip is not null)
        {
            throw new ValidationException([new ValidationFailure(propertyName,
                $"Không thể hủy ca vì chuyến {uncoveredTrip.TripCode} sẽ còn dưới "
                + $"{RequiredOnBoardStaffCount} nhân viên OnBoard. Hãy phân công người thay thế phủ toàn bộ chuyến trước.")]);
        }
    }

    public static async Task<IReadOnlyList<TripStaffAssignmentDto>> LoadTripOnBoardStaffAsync(
        IApplicationDbContext context,
        Trip trip,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!trip.BoatId.HasValue)
        {
            return [];
        }

        var assignments = await context.StaffWorkAssignments
            .AsNoTracking()
            .Include(x => x.StaffUser)
            .Where(x => x.AssignmentType == StaffWorkAssignmentType.Boat
                && x.BoatId == trip.BoatId.Value
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced
                && x.StaffUser.StaffType == StaffType.OnBoard
                && x.StartAt < trip.ArrivalTime
                && trip.DepartureTime < x.EndAt)
            .OrderBy(x => x.StartAt)
            .ThenBy(x => x.StaffUser.FullName)
            .ToListAsync(cancellationToken);

        return assignments.Select(x => ToTripStaffDto(x, now)).ToList();
    }

    public static TripStaffAssignmentDto ToTripStaffDto(
        StaffWorkAssignment assignment,
        DateTimeOffset now) =>
        new(
            assignment.Id,
            assignment.StaffUserId,
            assignment.StaffUser.FullName,
            assignment.StaffUser.StaffType?.ToString(),
            assignment.AssignmentType.ToString(),
            assignment.StartAt,
            assignment.EndAt,
            assignment.Status.ToString(),
            StaffWorkAssignmentSupport.ResolveShiftState(assignment, now),
            assignment.DutyRole);

    private static Task<int> CountCoveringOnBoardStaffAsync(
        IApplicationDbContext context,
        Guid boatId,
        DateTimeOffset departureTime,
        DateTimeOffset arrivalTime,
        CancellationToken cancellationToken) =>
        context.StaffWorkAssignments
            .AsNoTracking()
            .Where(x => x.AssignmentType == StaffWorkAssignmentType.Boat
                && x.BoatId == boatId
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced
                && x.StaffUser.StaffType == StaffType.OnBoard
                && x.StartAt <= departureTime
                && x.EndAt >= arrivalTime)
            .Select(x => x.StaffUserId)
            .Distinct()
            .CountAsync(cancellationToken);
}
