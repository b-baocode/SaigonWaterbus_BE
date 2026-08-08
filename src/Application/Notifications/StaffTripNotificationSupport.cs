using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Notifications;

/// <summary>
/// Notification vận hành dành cho staff OnBoard. Dữ liệu hiện tại vẫn được đọc qua
/// GET /api/operations/schedule; các hàm ở đây chỉ tạo event để mobile/FE cập nhật nhanh.
/// </summary>
public static class StaffTripNotificationSupport
{
    public static async Task<IReadOnlyList<Notification>> AddManagementTripStatusNotificationsAsync(
        IApplicationDbContext context,
        Trip trip,
        TripStatus oldStatus,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (trip.TripStatus == oldStatus)
        {
            return [];
        }

        var statusMessage = trip.TripStatus switch
        {
            TripStatus.Boarding => ("Chuyến bắt đầu đón khách", $"{NotificationSupport.DescribeTrip(trip)} đã bắt đầu đón khách.", NotificationTypes.OperationsTripBoarding),
            TripStatus.InProgress => ("Chuyến đã rời bến", $"{NotificationSupport.DescribeTrip(trip)} đã rời bến.", NotificationTypes.OperationsTripDeparted),
            TripStatus.Completed => ("Chuyến đã hoàn tất", $"{NotificationSupport.DescribeTrip(trip)} đã hoàn tất.", NotificationTypes.OperationsTripCompleted),
            TripStatus.Cancelled => ("Chuyến đã bị hủy", $"{NotificationSupport.DescribeTrip(trip)} đã bị hủy. {trip.StatusNote ?? "Vui lòng kiểm tra phương án điều hành."}", NotificationTypes.OperationsTripCancelled),
            TripStatus.Delayed => ("Chuyến đang bị trễ", $"{NotificationSupport.DescribeTrip(trip)} đang bị trễ. {trip.DelayReason ?? trip.StatusNote ?? "Vui lòng kiểm tra lịch vận hành."}", NotificationTypes.OperationsTripDelayed),
            _ => default
        };
        if (statusMessage == default)
        {
            return [];
        }

        var recipientIds = await context.Set<User>()
            .AsNoTracking()
            .Where(x => x.Status == UserStatus.Active
                && (x.Role.Code == Roles.AdminCode || x.Role.Code == Roles.ManagerCode))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var notifications = recipientIds
            .Select(userId => CreateNotification(
                userId,
                statusMessage.Item1,
                statusMessage.Item2,
                statusMessage.Item3,
                NotificationRelatedEntityTypes.Trip,
                trip.Id,
                now))
            .ToList();
        context.Set<Notification>().AddRange(notifications);
        return notifications;
    }

    public static async Task<IReadOnlyList<Notification>> AddTripStatusChangedNotificationsAsync(
        IApplicationDbContext context,
        Trip trip,
        TripStatus oldStatus,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (trip.TripStatus == oldStatus)
        {
            return [];
        }

        return trip.TripStatus switch
        {
            TripStatus.Boarding => await AddTripNotificationsAsync(
                context,
                trip,
                "Tàu đang chuẩn bị đón khách",
                $"{NotificationSupport.DescribeTrip(trip)} đang ở trạng thái đón khách.",
                NotificationTypes.StaffTripUpcoming,
                now,
                cancellationToken),
            TripStatus.InProgress => await AddTripNotificationsAsync(
                context,
                trip,
                "Tàu đã rời bến",
                $"{NotificationSupport.DescribeTrip(trip)} đã rời bến và đang chạy.",
                NotificationTypes.StaffTripDeparted,
                now,
                cancellationToken),
            TripStatus.Completed => await AddTripNotificationsAsync(
                context,
                trip,
                "Chuyến đã hoàn tất",
                $"{NotificationSupport.DescribeTrip(trip)} đã cập bến và hoàn tất.",
                NotificationTypes.StaffTripCompleted,
                now,
                cancellationToken),
            TripStatus.Cancelled => await AddTripNotificationsAsync(
                context,
                trip,
                "Chuyến đã bị hủy",
                $"{NotificationSupport.DescribeTrip(trip)} đã bị hủy. Vui lòng kiểm tra phương án điều hành.",
                NotificationTypes.StaffTripCancelled,
                now,
                cancellationToken),
            TripStatus.Delayed => await AddTripNotificationsAsync(
                context,
                trip,
                "Chuyến đang bị trễ",
                $"{NotificationSupport.DescribeTrip(trip)} đang bị trễ. {trip.DelayReason ?? trip.StatusNote ?? "Vui lòng kiểm tra lịch vận hành."}",
                NotificationTypes.TripDelayed,
                now,
                cancellationToken),
            _ => []
        };
    }

    public static async Task<IReadOnlyList<Notification>> AddTripStopEventNotificationsAsync(
        IApplicationDbContext context,
        Trip trip,
        TripStop tripStop,
        string eventType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (eventType is not ("Arriving" or "Arrived") || trip.TripStops.Count == 0)
        {
            return [];
        }

        var lastStopOrder = trip.TripStops.Max(x => x.StopOrder);
        if (tripStop.StopOrder != lastStopOrder)
        {
            return [];
        }

        var stationName = tripStop.Station?.StationName ?? "bến cuối";
        var isArrived = eventType == "Arrived";
        return await AddTripNotificationsAsync(
            context,
            trip,
            isArrived ? "Tàu đã cập bến" : "Tàu sắp cập bến",
            isArrived
                ? $"{NotificationSupport.DescribeTrip(trip)} đã cập bến {stationName}."
                : $"{NotificationSupport.DescribeTrip(trip)} sắp cập bến {stationName}.",
            isArrived ? NotificationTypes.StaffTripArrived : NotificationTypes.StaffTripArriving,
            now,
            cancellationToken);
    }

    public static async Task<IReadOnlyList<Notification>> AddPassengerScanNotificationsAsync(
        IApplicationDbContext context,
        Guid tripId,
        bool isCheckIn,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var trip = await context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Route)
            .SingleOrDefaultAsync(x => x.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return [];
        }

        var ticketStatuses = await context.Set<Ticket>()
            .AsNoTracking()
            .Where(x =>
                (x.BookingPassenger != null
                    && (x.BookingPassenger.TripId == tripId
                        || (!x.BookingPassenger.TripId.HasValue && x.Booking.TripId == tripId)))
                || (x.BookingPassenger == null && x.Booking.TripId == tripId))
            .Select(x => x.TicketStatus)
            .ToListAsync(cancellationToken);

        var boarded = ticketStatuses.Count(x => x is TicketStatus.CheckedIn or TicketStatus.CheckedOut);
        var onboard = ticketStatuses.Count(x => x == TicketStatus.CheckedIn);
        var alighted = ticketStatuses.Count(x => x == TicketStatus.CheckedOut);
        var actionText = isCheckIn ? "Khách vừa lên tàu" : "Khách vừa xuống tàu";
        var body = $"{actionText} {NotificationSupport.DescribeTrip(trip)}. "
            + $"Đã lên: {boarded}, đang trên tàu: {onboard}, đã xuống: {alighted}.";

        return await AddTripNotificationsAsync(
            context,
            trip,
            isCheckIn ? "Cập nhật khách lên tàu" : "Cập nhật khách xuống tàu",
            body,
            isCheckIn ? NotificationTypes.StaffPassengerBoarded : NotificationTypes.StaffPassengerAlighted,
            now,
            cancellationToken);
    }

    public static async Task<IReadOnlyList<Notification>> AddDueOperationalNotificationsAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        TimeSpan lookBack,
        TimeSpan lookAhead,
        CancellationToken cancellationToken)
    {
        var assignments = await context.Set<StaffWorkAssignment>()
            .AsNoTracking()
            .Include(x => x.StaffUser)
            .Where(x => x.AssignmentType == StaffWorkAssignmentType.Boat
                && x.BoatId.HasValue
                && x.Status == StaffWorkAssignmentStatus.Scheduled
                && x.StaffUser.Status == UserStatus.Active
                && x.StaffUser.StaffType == StaffType.OnBoard
                && x.EndAt >= now.Subtract(lookBack)
                && x.StartAt <= now.Add(lookAhead))
            .ToListAsync(cancellationToken);
        if (assignments.Count == 0)
        {
            return [];
        }

        var assignmentIds = assignments.Select(x => x.Id).ToArray();
        var boatIds = assignments.Select(x => x.BoatId!.Value).Distinct().ToArray();
        var trips = await context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Route)
            .Where(x => x.BoatId.HasValue
                && boatIds.Contains(x.BoatId.Value)
                && x.ArrivalTime >= now.Subtract(lookBack)
                && x.DepartureTime <= now.Add(lookAhead)
                && x.TripStatus != TripStatus.Completed)
            .ToListAsync(cancellationToken);
        var tripIds = trips.Select(x => x.Id).ToArray();

        var existing = await context.Set<Notification>()
            .AsNoTracking()
            .Where(x =>
                (x.RelatedEntityType == NotificationRelatedEntityTypes.StaffAssignment
                    && x.RelatedEntityId.HasValue
                    && assignmentIds.Contains(x.RelatedEntityId.Value)
                    && (x.Type == NotificationTypes.StaffShiftStarting || x.Type == NotificationTypes.StaffShiftEnding))
                || (x.RelatedEntityType == NotificationRelatedEntityTypes.Trip
                    && x.RelatedEntityId.HasValue
                    && tripIds.Contains(x.RelatedEntityId.Value)
                    && (x.Type == NotificationTypes.StaffTripUpcoming || x.Type == NotificationTypes.StaffTripArriving)))
            .Select(x => new { x.UserId, x.Type, x.RelatedEntityId })
            .ToListAsync(cancellationToken);
        var existingKeys = existing
            .Where(x => x.RelatedEntityId.HasValue)
            .Select(x => (x.UserId, x.Type, RelatedId: x.RelatedEntityId!.Value))
            .ToHashSet();

        var created = new List<Notification>();
        foreach (var assignment in assignments)
        {
            AddIfDue(
                assignment.StaffUserId,
                assignment.StartAt,
                NotificationTypes.StaffShiftStarting,
                NotificationRelatedEntityTypes.StaffAssignment,
                assignment.Id,
                "Ca làm sắp bắt đầu",
                $"Ca Boat của bạn bắt đầu lúc {NotificationSupport.FormatVietnamTime(assignment.StartAt)}.");
            AddIfDue(
                assignment.StaffUserId,
                assignment.EndAt,
                NotificationTypes.StaffShiftEnding,
                NotificationRelatedEntityTypes.StaffAssignment,
                assignment.Id,
                "Ca làm sắp kết thúc",
                $"Ca Boat của bạn kết thúc lúc {NotificationSupport.FormatVietnamTime(assignment.EndAt)}.");
        }

        foreach (var trip in trips)
        {
            var matchingAssignments = assignments.Where(x =>
                x.BoatId == trip.BoatId
                && x.StartAt < trip.ArrivalTime
                && trip.DepartureTime < x.EndAt);
            var departure = trip.AdjustedDepartureTime ?? trip.DepartureTime;
            var arrival = trip.AdjustedArrivalTime ?? trip.ArrivalTime;
            foreach (var assignment in matchingAssignments)
            {
                if (trip.TripStatus is not (TripStatus.InProgress or TripStatus.Completed or TripStatus.Cancelled)
                    && IsDue(departure))
                {
                    AddTripIfMissing(
                        assignment.StaffUserId,
                        NotificationTypes.StaffTripUpcoming,
                        trip.Id,
                        "Chuyến sắp khởi hành",
                        $"{NotificationSupport.DescribeTrip(trip)} dự kiến rời bến lúc {NotificationSupport.FormatVietnamTime(departure)}.");
                }

                if (trip.TripStatus is TripStatus.Boarding or TripStatus.InProgress or TripStatus.Delayed
                    && IsDue(arrival))
                {
                    AddTripIfMissing(
                        assignment.StaffUserId,
                        NotificationTypes.StaffTripArriving,
                        trip.Id,
                        "Chuyến sắp cập bến",
                        $"{NotificationSupport.DescribeTrip(trip)} dự kiến cập bến lúc {NotificationSupport.FormatVietnamTime(arrival)}.");
                }
            }
        }

        return created;

        bool IsDue(DateTimeOffset value) => value >= now.Subtract(lookBack) && value <= now.Add(lookAhead);

        void AddIfDue(
            Guid userId,
            DateTimeOffset dueAt,
            string type,
            string relatedType,
            Guid relatedId,
            string title,
            string body)
        {
            if (!IsDue(dueAt) || existingKeys.Contains((userId, type, relatedId)))
            {
                return;
            }

            var notification = CreateNotification(userId, title, body, type, relatedType, relatedId, now);
            context.Set<Notification>().Add(notification);
            created.Add(notification);
            existingKeys.Add((userId, type, relatedId));
        }

        void AddTripIfMissing(Guid userId, string type, Guid tripIdValue, string title, string body)
        {
            if (existingKeys.Contains((userId, type, tripIdValue)))
            {
                return;
            }

            var notification = CreateNotification(
                userId,
                title,
                body,
                type,
                NotificationRelatedEntityTypes.Trip,
                tripIdValue,
                now);
            context.Set<Notification>().Add(notification);
            created.Add(notification);
            existingKeys.Add((userId, type, tripIdValue));
        }
    }

    private static async Task<IReadOnlyList<Notification>> AddTripNotificationsAsync(
        IApplicationDbContext context,
        Trip trip,
        string title,
        string body,
        string type,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!trip.BoatId.HasValue)
        {
            return [];
        }

        var staffIds = await context.Set<StaffWorkAssignment>()
            .AsNoTracking()
            .Where(x => x.AssignmentType == StaffWorkAssignmentType.Boat
                && x.BoatId == trip.BoatId.Value
                && x.Status == StaffWorkAssignmentStatus.Scheduled
                && x.StaffUser.Status == UserStatus.Active
                && x.StaffUser.StaffType == StaffType.OnBoard
                && x.StartAt < trip.ArrivalTime
                && trip.DepartureTime < x.EndAt)
            .Select(x => x.StaffUserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var notifications = staffIds
            .Select(userId => CreateNotification(
                userId,
                title,
                body,
                type,
                NotificationRelatedEntityTypes.Trip,
                trip.Id,
                now))
            .ToList();
        context.Set<Notification>().AddRange(notifications);
        return notifications;
    }

    private static Notification CreateNotification(
        Guid userId,
        string title,
        string body,
        string type,
        string relatedEntityType,
        Guid relatedEntityId,
        DateTimeOffset now) => new()
    {
        UserId = userId,
        Title = title,
        Body = body,
        Type = type,
        RelatedEntityType = relatedEntityType,
        RelatedEntityId = relatedEntityId,
        CreatedAt = now
    };
}
