using System.Globalization;
using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin,Staff")]
public sealed record StartTripDelayCommand(
    Guid TripId,
    string? Reason = null,
    int? StartStopOrder = null) : IRequest<TripDelayActionResultDto>;

[Authorize(Roles = "Admin,Staff")]
public sealed record ResumeTripDelayCommand(
    Guid TripId,
    string? Note = null) : IRequest<TripDelayActionResultDto>;

public sealed record TripDelayActionResultDto(
    TripDetailDto Trip,
    TripDelayInfoDto? DelayInfo,
    IReadOnlyList<TripDelayAffectedTripDto> AffectedTrips);

public sealed record TripDelayAffectedTripDto(
    Guid TripId,
    string TripCode,
    int AddedDelayMinutes,
    int TotalDelayMinutes,
    DateTimeOffset? AdjustedDepartureTime,
    DateTimeOffset? AdjustedArrivalTime);

public sealed class StartTripDelayCommandValidator : AbstractValidator<StartTripDelayCommand>
{
    public StartTripDelayCommandValidator()
    {
        RuleFor(x => x.StartStopOrder)
            .GreaterThan(0)
            .When(x => x.StartStopOrder.HasValue)
            .WithMessage("startStopOrder phải lớn hơn 0.");

        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .When(x => !string.IsNullOrWhiteSpace(x.Reason));
    }
}

public sealed class ResumeTripDelayCommandValidator : AbstractValidator<ResumeTripDelayCommand>
{
    public ResumeTripDelayCommandValidator()
    {
        RuleFor(x => x.Note)
            .MaximumLength(500)
            .When(x => !string.IsNullOrWhiteSpace(x.Note));
    }
}

public sealed class StartTripDelayCommandHandler
    : IRequestHandler<StartTripDelayCommand, TripDelayActionResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ITripDelayRealtimeNotifier _tripDelayRealtimeNotifier;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public StartTripDelayCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ITripDelayRealtimeNotifier? tripDelayRealtimeNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _tripDelayRealtimeNotifier = tripDelayRealtimeNotifier ?? NullTripDelayRealtimeNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<TripDelayActionResultDto> Handle(
        StartTripDelayCommand request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var trip = await TripDelayCommandSupport.LoadTripForDelayAsync(
            _context,
            request.TripId,
            cancellationToken);

        await TripDelayCommandSupport.EnsureActorCanDelayTripAsync(
            _context,
            actor,
            trip,
            now,
            cancellationToken);
        TripDelayCommandSupport.EnsureTripCanBeDelayed(trip);

        var startStopOrder = request.StartStopOrder
            ?? trip.DelayStartStopOrder
            ?? TripDelaySupport.ResolveDelayStartStopOrder(trip);
        TripDelayCommandSupport.EnsureStopOrderExists(trip, startStopOrder);
        var reason = TripDelayCommandSupport.NormalizeOptionalText(request.Reason)
            ?? trip.DelayReason
            ?? "Nhân viên tàu báo chuyến đang bị trễ.";

        if (trip.DelayStartedAt.HasValue && !trip.DelayEndedAt.HasValue)
        {
            trip.DelayReason = reason;
            trip.DelayStartStopOrder = startStopOrder;
            trip.StatusNote = reason;
            await _context.SaveChangesAsync(cancellationToken);
            var activeResult = await TripDelayCommandSupport.BuildResultAsync(
                _context,
                trip,
                [],
                now,
                cancellationToken);
            await _tripDelayRealtimeNotifier.PublishUpdatedAsync(
                TripDelayCommandSupport.BuildRealtimeEvent(trip, activeResult.AffectedTrips),
                cancellationToken);
            return activeResult;
        }

        var oldStatus = trip.TripStatus;
        trip.DelayStartedAt = now;
        trip.DelayEndedAt = null;
        trip.DelayStartStopOrder = startStopOrder;
        trip.DelayReason = reason;
        trip.TripStatus = TripStatus.Delayed;
        trip.StatusNote = reason;

        var staffNotifications = await StaffTripNotificationSupport.AddTripStatusChangedNotificationsAsync(
            _context,
            trip,
            oldStatus,
            now,
            cancellationToken);
        staffNotifications = staffNotifications
            .Concat(await StaffTripNotificationSupport.AddManagementTripStatusNotificationsAsync(
                _context,
                trip,
                oldStatus,
                now,
                cancellationToken))
            .ToList();

        await _context.SaveChangesAsync(cancellationToken);
        await NotificationSupport.PublishCreatedAsync(
            _notificationRealtimeNotifier,
            staffNotifications,
            cancellationToken);

        var result = await TripDelayCommandSupport.BuildResultAsync(
            _context,
            trip,
            [],
            now,
            cancellationToken);
        await _tripDelayRealtimeNotifier.PublishUpdatedAsync(
            TripDelayCommandSupport.BuildRealtimeEvent(trip, result.AffectedTrips),
            cancellationToken);
        return result;
    }
}

public sealed class ResumeTripDelayCommandHandler
    : IRequestHandler<ResumeTripDelayCommand, TripDelayActionResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ITripDelayRealtimeNotifier _tripDelayRealtimeNotifier;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public ResumeTripDelayCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ITripDelayRealtimeNotifier? tripDelayRealtimeNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _tripDelayRealtimeNotifier = tripDelayRealtimeNotifier ?? NullTripDelayRealtimeNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<TripDelayActionResultDto> Handle(
        ResumeTripDelayCommand request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var trip = await TripDelayCommandSupport.LoadTripForDelayAsync(
            _context,
            request.TripId,
            cancellationToken);

        await TripDelayCommandSupport.EnsureActorCanDelayTripAsync(
            _context,
            actor,
            trip,
            now,
            cancellationToken);
        TripDelayCommandSupport.EnsureTripCanBeDelayed(trip);

        if (!trip.DelayStartedAt.HasValue || trip.DelayEndedAt.HasValue)
        {
            throw new ValidationException([new ValidationFailure("delay",
                "Trip chưa có delay đang active để tiếp tục.")]);
        }

        var delayStartedAt = trip.DelayStartedAt.Value;
        var oldStatus = trip.TripStatus;
        var activeDelayMinutes = Math.Max(
            1,
            (int)Math.Ceiling((now - delayStartedAt).TotalMinutes));
        var totalDelayMinutes = trip.DelayMinutes + activeDelayMinutes;
        var startStopOrder = trip.DelayStartStopOrder ?? TripDelaySupport.ResolveDelayStartStopOrder(trip);
        TripDelayCommandSupport.EnsureStopOrderExists(trip, startStopOrder);

        var note = TripDelayCommandSupport.NormalizeOptionalText(request.Note);
        var reason = trip.DelayReason
            ?? note
            ?? "Nhân viên tàu báo chuyến bị trễ.";

        TripDelaySupport.ApplyDelayToTrip(
            trip,
            totalDelayMinutes,
            reason,
            startStopOrder);
        trip.DelayStartedAt = delayStartedAt;
        trip.DelayEndedAt = now;
        trip.DelayStartStopOrder = startStopOrder;
        trip.StatusNote = note ?? $"Tàu tiếp tục hành trình sau khi trễ {activeDelayMinutes} phút.";
        trip.TripStatus = TripDelaySupport.ResolveResumedStatus(trip, now);

        var affectedTrips = await LoadAndApplyFutureTripDelaysAsync(
            trip,
            totalDelayMinutes,
            cancellationToken);
        trip.DelayPropagationMinutes = affectedTrips
            .Select(x => x.Dto.AddedDelayMinutes)
            .DefaultIfEmpty(0)
            .Max();

        var notifications = new List<Notification>();
        notifications.AddRange(await AddCurrentTripDelayNotificationsAsync(
            trip,
            totalDelayMinutes,
            startStopOrder,
            now,
            cancellationToken));
        foreach (var affectedTrip in affectedTrips)
        {
            notifications.AddRange(await AddFutureTripDelayNotificationsAsync(
                affectedTrip.Trip,
                affectedTrip.Dto.AddedDelayMinutes,
                now,
                cancellationToken));
        }
        notifications.AddRange(await StaffTripNotificationSupport.AddTripStatusChangedNotificationsAsync(
            _context,
            trip,
            oldStatus,
            now,
            cancellationToken));
        notifications.AddRange(await StaffTripNotificationSupport.AddManagementTripStatusNotificationsAsync(
            _context,
            trip,
            oldStatus,
            now,
            cancellationToken));

        await _context.SaveChangesAsync(cancellationToken);
        await NotificationSupport.PublishCreatedAsync(
            _notificationRealtimeNotifier,
            notifications,
            cancellationToken);

        var result = await TripDelayCommandSupport.BuildResultAsync(
            _context,
            trip,
            affectedTrips.Select(x => x.Dto).ToArray(),
            now,
            cancellationToken);
        await _tripDelayRealtimeNotifier.PublishUpdatedAsync(
            TripDelayCommandSupport.BuildRealtimeEvent(trip, result.AffectedTrips),
            cancellationToken);
        return result;
    }

    private async Task<IReadOnlyList<AffectedTripWithEntity>> LoadAndApplyFutureTripDelaysAsync(
        Trip sourceTrip,
        int sourceTotalDelayMinutes,
        CancellationToken cancellationToken)
    {
        if (!sourceTrip.BoatId.HasValue)
        {
            return [];
        }

        var reason = $"Bị ảnh hưởng bởi chuyến {sourceTrip.TripCode} trễ {sourceTotalDelayMinutes} phút.";
        var futureTrips = await _context.Set<Trip>()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
                .ThenInclude(x => x.Station)
            .Where(x => x.Id != sourceTrip.Id
                && x.BoatId == sourceTrip.BoatId
                && x.OperatingDate == sourceTrip.OperatingDate
                && x.DepartureTime > sourceTrip.DepartureTime
                && x.TripStatus != TripStatus.Completed
                && x.TripStatus != TripStatus.Cancelled)
            .OrderBy(x => x.DepartureTime)
            .ThenBy(x => x.TripCode)
            .ToListAsync(cancellationToken);

        var affectedTrips = new List<AffectedTripWithEntity>();
        var previousAvailableAt = TripDelaySupport.ResolveAdjustedArrival(sourceTrip);
        foreach (var futureTrip in futureTrips)
        {
            var previousDelayMinutes = futureTrip.DelayMinutes;
            var totalDelayMinutes = TripDelaySupport.CalculateCascadedTotalDelayMinutes(
                futureTrip,
                previousAvailableAt);
            if (totalDelayMinutes <= previousDelayMinutes)
            {
                previousAvailableAt = TripDelaySupport.ResolveAdjustedArrival(futureTrip);
                continue;
            }

            TripDelaySupport.ApplyTotalDelayToFutureTrip(futureTrip, totalDelayMinutes, reason);
            affectedTrips.Add(new AffectedTripWithEntity(
                futureTrip,
                new TripDelayAffectedTripDto(
                    futureTrip.Id,
                    futureTrip.TripCode,
                    totalDelayMinutes - previousDelayMinutes,
                    futureTrip.DelayMinutes,
                    futureTrip.AdjustedDepartureTime,
                    futureTrip.AdjustedArrivalTime)));
            previousAvailableAt = TripDelaySupport.ResolveAdjustedArrival(futureTrip);
        }

        return affectedTrips;
    }

    private async Task<IReadOnlyList<Notification>> AddCurrentTripDelayNotificationsAsync(
        Trip trip,
        int totalDelayMinutes,
        int startStopOrder,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recipients = await LoadConfirmedPassengerBookingsAsync(
            trip,
            cancellationToken,
            passengerFilter: x =>
                (!x.FromStopOrder.HasValue && !x.ToStopOrder.HasValue)
                || (x.ToStopOrder ?? int.MaxValue) > startStopOrder
                || (x.FromStopOrder ?? 0) >= startStopOrder);

        return AddTripDelayNotifications(
            recipients,
            trip,
            $"Chuyến {trip.TripCode} bị trễ {totalDelayMinutes} phút. "
            + "Vui lòng theo dõi giờ rời bến mới trên vé.",
            now);
    }

    private async Task<IReadOnlyList<Notification>> AddFutureTripDelayNotificationsAsync(
        Trip trip,
        int addedDelayMinutes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recipients = await LoadConfirmedPassengerBookingsAsync(
            trip,
            cancellationToken,
            passengerFilter: _ => true);

        return AddTripDelayNotifications(
            recipients,
            trip,
            $"Chuyến {trip.TripCode} dự kiến khởi hành trễ thêm {addedDelayMinutes} phút do tàu về muộn. "
            + "Vui lòng theo dõi giờ rời bến mới trên vé.",
            now);
    }

    private async Task<IReadOnlyList<DelayNotificationRecipient>> LoadConfirmedPassengerBookingsAsync(
        Trip trip,
        CancellationToken cancellationToken,
        Func<PassengerDelayCandidate, bool> passengerFilter)
    {
        var passengers = await _context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => x.Booking.UserId != null
                && x.Booking.BookingStatus == BookingStatus.Confirmed
                && (x.TripId == trip.Id
                    || (!x.TripId.HasValue && x.Booking.TripId == trip.Id)
                    || (x.TripSeat != null && x.TripSeat.TripId == trip.Id)))
            .Select(x => new PassengerDelayCandidate(
                x.BookingId,
                x.Booking.UserId!.Value,
                x.Booking.BookingCode,
                x.FromStopOrder,
                x.ToStopOrder))
            .ToListAsync(cancellationToken);

        return passengers
            .Where(passengerFilter)
            .GroupBy(x => x.BookingId)
            .Select(g =>
            {
                var first = g.First();
                return new DelayNotificationRecipient(
                    first.BookingId,
                    first.UserId,
                    first.BookingCode,
                    ResolveBoardingDeparture(trip, first.FromStopOrder));
            })
            .ToArray();
    }

    private IReadOnlyList<Notification> AddTripDelayNotifications(
        IReadOnlyList<DelayNotificationRecipient> recipients,
        Trip trip,
        string body,
        DateTimeOffset now)
    {
        if (recipients.Count == 0)
        {
            return [];
        }

        var notifications = new List<Notification>(recipients.Count);
        foreach (var recipient in recipients)
        {
            var notification = new Notification
            {
                UserId = recipient.UserId,
                Title = "Chuyến đi bị trễ",
                Body = $"{body}{FormatExpectedDeparture(recipient.ExpectedBoardingDeparture)} "
                    + $"Booking {recipient.BookingCode} bị ảnh hưởng.",
                Type = NotificationTypes.TripDelayed,
                RelatedEntityType = NotificationRelatedEntityTypes.Booking,
                RelatedEntityId = recipient.BookingId,
                CreatedAt = now
            };
            _context.Set<Notification>().Add(notification);
            notifications.Add(notification);
        }

        return notifications;
    }

    private static DateTimeOffset? ResolveBoardingDeparture(Trip trip, int? fromStopOrder)
    {
        if (trip.TripStops.Count == 0)
        {
            return trip.AdjustedDepartureTime ?? trip.DepartureTime;
        }

        var stop = fromStopOrder.HasValue
            ? trip.TripStops.FirstOrDefault(x => x.StopOrder == fromStopOrder.Value)
            : trip.TripStops.OrderBy(x => x.StopOrder).FirstOrDefault();
        return stop?.AdjustedDepartureTime
            ?? stop?.PlannedDepartureTime
            ?? stop?.AdjustedArrivalTime
            ?? stop?.PlannedArrivalTime
            ?? trip.AdjustedDepartureTime
            ?? trip.DepartureTime;
    }

    private static string FormatExpectedDeparture(DateTimeOffset? expectedDeparture)
    {
        if (!expectedDeparture.HasValue)
        {
            return string.Empty;
        }

        var vietnamTime = expectedDeparture.Value.ToOffset(TimeSpan.FromHours(7));
        return $" Giờ rời bến dự kiến: {vietnamTime.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture)}.";
    }

    private sealed record AffectedTripWithEntity(Trip Trip, TripDelayAffectedTripDto Dto);

    private sealed record PassengerDelayCandidate(
        Guid BookingId,
        Guid UserId,
        string BookingCode,
        int? FromStopOrder,
        int? ToStopOrder);

    private sealed record DelayNotificationRecipient(
        Guid BookingId,
        Guid UserId,
        string BookingCode,
        DateTimeOffset? ExpectedBoardingDeparture);
}

internal static class TripDelayCommandSupport
{
    public static async Task<Trip> LoadTripForDelayAsync(
        IApplicationDbContext context,
        Guid tripId,
        CancellationToken cancellationToken)
    {
        return await context.Set<Trip>()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == tripId, cancellationToken)
            ?? throw new NotFoundException("Trip không tồn tại.");
    }

    public static async Task EnsureActorCanDelayTripAsync(
        IApplicationDbContext context,
        User actor,
        Trip trip,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        if (!AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        if (actor.StaffType != StaffType.OnBoard)
        {
            throw new ValidationException([new ValidationFailure("staffWorkAssignment",
                "Chỉ nhân viên OnBoard trên tàu mới được bấm delay chuyến.")]);
        }

        if (!trip.BoatId.HasValue)
        {
            throw new ValidationException([new ValidationFailure("trip",
                "Trip chưa gắn tàu nên nhân viên không thể bấm delay.")]);
        }

        var hasActiveBoatAssignment = await context.StaffWorkAssignments
            .AsNoTracking()
            .AnyAsync(x => x.StaffUserId == actor.Id
                && x.AssignmentType == StaffWorkAssignmentType.Boat
                && x.BoatId == trip.BoatId.Value
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced
                && x.StartAt <= now
                && x.EndAt >= now,
                cancellationToken);

        if (!hasActiveBoatAssignment)
        {
            throw new ValidationException([new ValidationFailure("staffWorkAssignment",
                "Nhân viên này chưa có ca OnBoard đang active trên tàu của trip.")]);
        }
    }

    public static void EnsureTripCanBeDelayed(Trip trip)
    {
        if (trip.TripStatus is TripStatus.Completed or TripStatus.Cancelled)
        {
            throw new ValidationException([new ValidationFailure("tripStatus",
                "Trip đã Completed/Cancelled nên không thể bấm delay.")]);
        }

        if (!trip.BoatId.HasValue)
        {
            throw new ValidationException([new ValidationFailure("boat",
                "Trip chưa gắn tàu nên không thể bấm delay.")]);
        }

        if (trip.Boat?.Status == BoatStatus.UnderMaintenance)
        {
            throw new ValidationException([new ValidationFailure("boat",
                "Tàu đang bảo trì, không được kéo dài delay. Hãy dùng API replan để chọn tàu thay thế hoặc xác nhận hủy chuyến.")]);
        }
    }

    public static void EnsureStopOrderExists(Trip trip, int stopOrder)
    {
        var hasStop = trip.TripStops.Count > 0
            ? trip.TripStops.Any(x => x.StopOrder == stopOrder)
            : trip.Route.RouteStops.Any(x => x.StopOrder == stopOrder);
        if (!hasStop)
        {
            throw new ValidationException([new ValidationFailure("startStopOrder",
                "startStopOrder không thuộc lịch dừng của trip.")]);
        }
    }

    public static async Task<TripDelayActionResultDto> BuildResultAsync(
        IApplicationDbContext context,
        Trip trip,
        IReadOnlyList<TripDelayAffectedTripDto> affectedTrips,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var totalPassengerCount = await context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => x.TripId == trip.Id || (!x.TripId.HasValue && x.Booking.TripId == trip.Id))
            .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
            .CountAsync(cancellationToken);

        var detail = UpdateTripStatusCommandHandler.ToDetailDto(
            trip,
            sourceBooking: null,
            TripStopScheduleSupport.BuildStopDtos(trip),
            onBoardStaff: null,
            totalPassengerCount);

        return new TripDelayActionResultDto(
            detail,
            TripDelaySupport.ToDelayInfoDto(trip),
            affectedTrips);
    }

    public static TripDelayRealtimeEvent BuildRealtimeEvent(
        Trip trip,
        IReadOnlyList<TripDelayAffectedTripDto> affectedTrips)
    {
        return new TripDelayRealtimeEvent(
            trip.Id,
            trip.TripCode,
            trip.BoatId,
            trip.Boat?.Code,
            trip.TripStatus.ToString(),
            trip.AdjustedDepartureTime,
            trip.AdjustedArrivalTime,
            trip.DelayMinutes,
            trip.DelayReason,
            trip.DelayStartedAt.HasValue && !trip.DelayEndedAt.HasValue,
            trip.DelayStartedAt,
            trip.DelayEndedAt,
            trip.DelayStartStopOrder,
            trip.DelayPropagationMinutes,
            affectedTrips
                .Select(x => new TripDelayAffectedTripRealtimeEvent(
                    x.TripId,
                    x.TripCode,
                    x.AddedDelayMinutes,
                    x.TotalDelayMinutes,
                    x.AdjustedDepartureTime,
                    x.AdjustedArrivalTime))
                .ToArray());
    }

    public static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
