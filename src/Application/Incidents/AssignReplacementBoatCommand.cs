using System.Globalization;
using FluentValidation.Results;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Incidents;

public sealed record AssignReplacementBoatCommand(
    Guid IncidentId,
    Guid RescueBoatId,
    Guid? ReplacementBoatId,
    int? DelayMinutes,
    string? Note) : IRequest<IncidentDto>;

public sealed class AssignReplacementBoatCommandValidator : AbstractValidator<AssignReplacementBoatCommand>
{
    public AssignReplacementBoatCommandValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty();
        RuleFor(x => x.RescueBoatId).NotEmpty();
        RuleFor(x => x.ReplacementBoatId).NotEmpty().When(x => x.ReplacementBoatId.HasValue);
        RuleFor(x => x.DelayMinutes).GreaterThanOrEqualTo(0).When(x => x.DelayMinutes.HasValue);
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class AssignReplacementBoatCommandHandler : IRequestHandler<AssignReplacementBoatCommand, IncidentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly IIncidentRealtimeNotifier _realtimeNotifier;
    private readonly IIncidentGpsHookNotifier _gpsHookNotifier;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public AssignReplacementBoatCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IIncidentRealtimeNotifier? realtimeNotifier = null,
        IIncidentGpsHookNotifier? gpsHookNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullIncidentRealtimeNotifier.Instance;
        _gpsHookNotifier = gpsHookNotifier ?? NullIncidentGpsHookNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<IncidentDto> Handle(
        AssignReplacementBoatCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await IncidentSupport.EnsureCurrentUserCanResolveIncidentAsync(
            _context,
            _userContext,
            cancellationToken);

        var incident = await LoadIncidentQuery()
            .SingleOrDefaultAsync(x => x.Id == request.IncidentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy sự cố.");

        if (incident.BoatId == request.RescueBoatId)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.RescueBoatId),
                "Tàu cứu hộ không được trùng với tàu gặp sự cố.")]);
        }

        var rescueBoat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.RescueBoatId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tàu cứu hộ.");
        EnsureRescueBoatReady(rescueBoat);

        var passengerImpact = await IncidentSupport.BuildPassengerImpactPlanAsync(
            _context,
            incident,
            cancellationToken);

        Boat? replacementBoat = null;
        var replacementRequired = passengerImpact.AffectedPassengerCount > 0;
        var replacementRequested = request.ReplacementBoatId.HasValue;
        if (replacementRequired && !replacementRequested)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ReplacementBoatId),
                BuildReplacementRequiredMessage(passengerImpact))]);
        }

        if (!replacementRequired && replacementRequested && incident.Trip is null)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ReplacementBoatId),
                "Sự cố không có khách bị ảnh hưởng hoặc trip cần chạy tiếp nên chỉ chọn tàu cứu hộ.")]);
        }

        if (replacementRequested)
        {
            var replacementBoatId = request.ReplacementBoatId.GetValueOrDefault();
            if (replacementBoatId == incident.BoatId)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ReplacementBoatId),
                    "Tàu thay thế không được trùng với tàu gặp sự cố.")]);
            }

            if (replacementBoatId == request.RescueBoatId)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ReplacementBoatId),
                    "Tàu thay thế chở khách không được trùng với tàu cứu hộ.")]);
            }

            replacementBoat = await _context.Boats
                .Include(x => x.Seats)
                .SingleOrDefaultAsync(x => x.Id == replacementBoatId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy tàu thay thế.");
            EnsurePassengerReplacementBoatReady(replacementBoat);

            if (replacementRequired && replacementBoat.SeatCount < passengerImpact.AffectedPassengerCount)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ReplacementBoatId),
                    $"Tàu thay thế không đủ ghế. Cần tối thiểu {passengerImpact.AffectedPassengerCount} ghế cho khách bị ảnh hưởng.")]);
            }
        }
        var assignedAt = _timeProvider.GetUtcNow();
        var delayMinutes = request.DelayMinutes.GetValueOrDefault();
        var estimatedResumeAt = ResolveEstimatedResumeAt(passengerImpact, assignedAt, delayMinutes);
        incident.RescueBoatId = rescueBoat.Id;
        incident.RescueBoat = rescueBoat;
        incident.RescueDispatchedAt = assignedAt;
        incident.RescueDispatchedByUserId = actor.Id;
        incident.RescueDispatchedByUser = actor;
        incident.ReplacementBoatId = replacementBoat?.Id;
        incident.ReplacementBoat = replacementBoat;
        incident.ReplacementAssignedAt = replacementBoat is null ? null : assignedAt;
        incident.ReplacementAssignedByUserId = replacementBoat is null ? null : actor.Id;
        incident.ReplacementAssignedByUser = replacementBoat is null ? null : actor;
        incident.ReplacementMissionType = passengerImpact.ReplacementMissionType;
        incident.ReplacementTargetStationId = passengerImpact.TargetStationId;
        incident.ReplacementTargetStopOrder = passengerImpact.TargetStopOrder;
        incident.ReplacementDelayMinutes = delayMinutes;
        incident.ReplacementEstimatedResumeAt = estimatedResumeAt;
        incident.ActiveTicketCountSnapshot = passengerImpact.ActiveTicketCount;
        incident.OnboardPassengerCountSnapshot = passengerImpact.OnboardPassengerCount;
        incident.FuturePassengerCountSnapshot = passengerImpact.FuturePassengerCount;
        incident.ReplacementNote = NormalizeNote(request.Note)
            ?? BuildDefaultReplacementNote(passengerImpact, replacementBoat);

        var createdNotifications = new List<Notification>();
        if (incident.Trip is not null)
        {
            if (replacementBoat is not null)
            {
                incident.Trip.BoatId = replacementBoat.Id;
                incident.Trip.Boat = replacementBoat;
            }

            if (incident.Trip.TripStatus is not TripStatus.Completed and not TripStatus.Cancelled)
            {
                if (delayMinutes > 0)
                {
                    incident.Trip.TripStatus = TripStatus.Delayed;
                }
            }

            ApplyTripDelay(incident.Trip, delayMinutes, incident.ReplacementNote);
            await ApplyTripStopDelayAsync(
                _context,
                incident.Trip,
                passengerImpact,
                delayMinutes,
                cancellationToken);
            var affectedFutureTrips = await ApplyFutureTripDelayIfNeededAsync(
                _context,
                incident,
                incident.Trip,
                delayMinutes,
                incident.ReplacementNote,
                cancellationToken);

            incident.Trip.StatusNote = incident.ReplacementNote
                ?? (replacementBoat is null
                    ? "Đã điều tàu cứu hộ cho sự cố."
                    : $"Đã điều tàu {replacementBoat.Name} thay thế.");

            if (delayMinutes > 0)
            {
                createdNotifications.AddRange(await AddIncidentDelayNotificationsAsync(
                    incident.Trip,
                    delayMinutes,
                    $"Chuyến {incident.Trip.TripCode} bị trễ {delayMinutes} phút do sự cố tàu.",
                    assignedAt,
                    cancellationToken));
            }

            foreach (var affectedTrip in affectedFutureTrips)
            {
                createdNotifications.AddRange(await AddIncidentDelayNotificationsAsync(
                    affectedTrip.Trip,
                    affectedTrip.AddedDelayMinutes,
                    $"Chuyến {affectedTrip.Trip.TripCode} dự kiến khởi hành trễ thêm {affectedTrip.AddedDelayMinutes} phút do tàu trước gặp sự cố.",
                    assignedAt,
                    cancellationToken));
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await NotificationSupport.PublishCreatedAsync(
            _notificationRealtimeNotifier,
            createdNotifications,
            cancellationToken);

        incident = await LoadIncidentQuery().SingleAsync(x => x.Id == request.IncidentId, cancellationToken);
        await IncidentSupport.PublishGpsHookAsync(
            _context,
            _gpsHookNotifier,
            incident,
            IncidentSupport.RescueDispatchedEvent,
            cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            IncidentSupport.ToRealtimeEvent(incident, IncidentSupport.RescueDispatchedEvent, incident.RescueDispatchedAt),
            cancellationToken);
        return IncidentSupport.ToDto(incident, incident.ActiveTicketCountSnapshot);
    }

    private static void EnsureRescueBoatReady(Boat rescueBoat)
    {
        if (rescueBoat.ServiceType != BoatServiceType.Rescue || rescueBoat.Status != BoatStatus.Active)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(AssignReplacementBoatCommand.RescueBoatId),
                "Tàu cứu hộ phải có serviceType Rescue và đang Active.")]);
        }
    }

    private static void EnsurePassengerReplacementBoatReady(Boat replacementBoat)
    {
        if (replacementBoat.ServiceType != BoatServiceType.Passenger
            || replacementBoat.Status != BoatStatus.Active
            || !BoatSupport.IsReadyForOperation(replacementBoat))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(AssignReplacementBoatCommand.ReplacementBoatId),
                "Tàu thay thế phải là Passenger, Active và đã setup đủ ghế.")]);
        }
    }

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static string BuildReplacementRequiredMessage(
        IncidentSupport.IncidentPassengerImpactPlan passengerImpact)
    {
        if (passengerImpact.OnboardPassengerCount > 0)
        {
            return $"Có {passengerImpact.OnboardPassengerCount} khách đang ở trên tàu nên phải chọn tàu chở khách thay thế.";
        }

        if (passengerImpact.FuturePassengerCount > 0 && passengerImpact.TargetStationName is not null)
        {
            return $"Có {passengerImpact.FuturePassengerCount} khách chờ ở bến {passengerImpact.TargetStationName} nên phải chọn tàu chở khách thay thế.";
        }

        return $"Chuyến có {passengerImpact.AffectedPassengerCount} khách bị ảnh hưởng nên phải chọn tàu chở khách thay thế.";
    }

    private static string? BuildDefaultReplacementNote(
        IncidentSupport.IncidentPassengerImpactPlan passengerImpact,
        Boat? replacementBoat)
    {
        if (replacementBoat is null)
        {
            return null;
        }

        if (passengerImpact.ReplacementMissionType == IncidentReplacementMissionTypes.TransferAtIncidentLocation)
        {
            return $"Đã điều tàu {replacementBoat.Name} tới vị trí sự cố để chuyển {passengerImpact.OnboardPassengerCount} khách đang trên tàu.";
        }

        if (passengerImpact.ReplacementMissionType == IncidentReplacementMissionTypes.ContinueFromStation
            && passengerImpact.TargetStationName is not null)
        {
            return passengerImpact.FuturePassengerCount > 0
                ? $"Đã điều tàu {replacementBoat.Name} tới bến {passengerImpact.TargetStationName} để đón {passengerImpact.FuturePassengerCount} khách chờ đi tiếp."
                : $"Đã điều tàu {replacementBoat.Name} tới bến {passengerImpact.TargetStationName} để tiếp tục hành trình.";
        }

        return passengerImpact.AffectedPassengerCount > 0
            ? $"Đã điều tàu {replacementBoat.Name} thay thế cho {passengerImpact.AffectedPassengerCount} khách bị ảnh hưởng."
            : $"Đã điều tàu {replacementBoat.Name} thay thế để tiếp tục hành trình.";
    }

    private static DateTimeOffset? ResolveEstimatedResumeAt(
        IncidentSupport.IncidentPassengerImpactPlan passengerImpact,
        DateTimeOffset assignedAt,
        int delayMinutes)
    {
        if (passengerImpact.ReplacementMissionType == IncidentReplacementMissionTypes.None)
        {
            return null;
        }

        var baseTime = passengerImpact.ReplacementMissionType == IncidentReplacementMissionTypes.ContinueFromStation
            ? passengerImpact.TargetPlannedDepartureAt
                ?? passengerImpact.TargetPlannedArrivalAt
                ?? assignedAt
            : assignedAt;

        return baseTime.AddMinutes(delayMinutes);
    }

    private static void ApplyTripDelay(
        Trip trip,
        int delayMinutes,
        string? delayReason)
    {
        trip.DelayMinutes = delayMinutes;
        trip.DelayReason = delayMinutes > 0 ? delayReason : null;
        trip.AdjustedDepartureTime = delayMinutes > 0
            ? trip.DepartureTime.AddMinutes(delayMinutes)
            : null;
        trip.AdjustedArrivalTime = delayMinutes > 0
            ? trip.ArrivalTime.AddMinutes(delayMinutes)
            : null;
    }

    private static async Task ApplyTripStopDelayAsync(
        IApplicationDbContext context,
        Trip trip,
        IncidentSupport.IncidentPassengerImpactPlan passengerImpact,
        int delayMinutes,
        CancellationToken cancellationToken)
    {
        var tripStops = await context.Set<TripStop>()
            .Where(x => x.TripId == trip.Id)
            .OrderBy(x => x.StopOrder)
            .ToListAsync(cancellationToken);
        if (tripStops.Count == 0)
        {
            return;
        }

        var startStopOrder = ResolveDelayStartStopOrder(tripStops, passengerImpact);
        foreach (var stop in tripStops.Where(x => x.StopOrder >= startStopOrder))
        {
            if (stop.ActualDepartureTime.HasValue)
            {
                continue;
            }

            stop.AdjustedArrivalTime = delayMinutes > 0
                ? stop.PlannedArrivalTime?.AddMinutes(delayMinutes)
                : null;
            stop.AdjustedDepartureTime = delayMinutes > 0
                ? stop.PlannedDepartureTime?.AddMinutes(delayMinutes)
                : null;
        }
    }

    private static int ResolveDelayStartStopOrder(
        IReadOnlyList<TripStop> tripStops,
        IncidentSupport.IncidentPassengerImpactPlan passengerImpact)
    {
        if (passengerImpact.TargetStopOrder.HasValue)
        {
            return passengerImpact.TargetStopOrder.Value;
        }

        return tripStops
            .Where(x => !x.ActualDepartureTime.HasValue)
            .Select(x => x.StopOrder)
            .DefaultIfEmpty(tripStops.Max(x => x.StopOrder))
            .Min();
    }

    private static async Task<IReadOnlyList<AffectedFutureTripDelay>> ApplyFutureTripDelayIfNeededAsync(
        IApplicationDbContext context,
        Incident incident,
        Trip sourceTrip,
        int delayMinutes,
        string? delayReason,
        CancellationToken cancellationToken)
    {
        if (!sourceTrip.BoatId.HasValue)
        {
            return [];
        }

        var futureTrips = await context.Set<Trip>()
            .Include(x => x.TripStops)
            .Where(x => x.Id != sourceTrip.Id
                && x.BoatId == incident.BoatId
                && x.OperatingDate == sourceTrip.OperatingDate
                && x.DepartureTime > sourceTrip.DepartureTime
                && x.TripStatus != TripStatus.Completed
                && x.TripStatus != TripStatus.Cancelled)
            .OrderBy(x => x.DepartureTime)
            .ToListAsync(cancellationToken);

        var affectedTrips = new List<AffectedFutureTripDelay>();
        var previousAvailableAt = TripDelaySupport.ResolveAdjustedArrival(sourceTrip);
        foreach (var futureTrip in futureTrips)
        {
            var oldDelayMinutes = futureTrip.DelayMinutes;
            var appliedDelayMinutes = TripDelaySupport.CalculateCascadedTotalDelayMinutes(
                futureTrip,
                previousAvailableAt);
            if (appliedDelayMinutes <= oldDelayMinutes)
            {
                previousAvailableAt = TripDelaySupport.ResolveAdjustedArrival(futureTrip);
                continue;
            }

            TripDelaySupport.ApplyTotalDelayToFutureTrip(
                futureTrip,
                appliedDelayMinutes,
                delayReason ?? $"Bị ảnh hưởng bởi chuyến {sourceTrip.TripCode} trễ {delayMinutes} phút.");

            affectedTrips.Add(new AffectedFutureTripDelay(
                futureTrip,
                appliedDelayMinutes - oldDelayMinutes));
            previousAvailableAt = TripDelaySupport.ResolveAdjustedArrival(futureTrip);
        }

        return affectedTrips;
    }

    private async Task<IReadOnlyList<Notification>> AddIncidentDelayNotificationsAsync(
        Trip trip,
        int changedDelayMinutes,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (changedDelayMinutes <= 0)
        {
            return [];
        }

        var recipients = await LoadTripDelayNotificationRecipientsAsync(trip, cancellationToken);
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
                Body = $"{body} Vui lòng theo dõi giờ rời bến mới trên vé."
                    + $"{FormatExpectedDeparture(recipient.ExpectedBoardingDeparture)} "
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

    private async Task<IReadOnlyList<IncidentDelayNotificationRecipient>> LoadTripDelayNotificationRecipientsAsync(
        Trip trip,
        CancellationToken cancellationToken)
    {
        var passengerCandidates = await _context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => x.Booking.UserId != null
                && x.Booking.BookingStatus == BookingStatus.Confirmed
                && (x.TripId == trip.Id
                    || (!x.TripId.HasValue && x.Booking.TripId == trip.Id)
                    || (x.TripSeat != null && x.TripSeat.TripId == trip.Id)))
            .Select(x => new IncidentDelayNotificationCandidate(
                x.BookingId,
                x.Booking.UserId!.Value,
                x.Booking.BookingCode,
                x.FromStopOrder))
            .ToListAsync(cancellationToken);

        var sourceBookingId = trip.SourceBookingId;
        var directBookingCandidates = await _context.Set<Booking>()
            .AsNoTracking()
            .Where(x => x.UserId != null
                && x.BookingStatus == BookingStatus.Confirmed
                && (x.TripId == trip.Id
                    || x.ReturnTripId == trip.Id
                    || (sourceBookingId.HasValue && x.Id == sourceBookingId.Value)))
            .Select(x => new IncidentDelayNotificationCandidate(
                x.Id,
                x.UserId!.Value,
                x.BookingCode,
                null))
            .ToListAsync(cancellationToken);

        return passengerCandidates
            .Concat(directBookingCandidates)
            .GroupBy(x => x.BookingId)
            .Select(g =>
            {
                var first = g.First();
                return new IncidentDelayNotificationRecipient(
                    first.BookingId,
                    first.UserId,
                    first.BookingCode,
                    ResolveBoardingDeparture(trip, first.FromStopOrder));
            })
            .ToArray();
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

    private IQueryable<Incident> LoadIncidentQuery() =>
        _context.Incidents
            .Include(x => x.Boat)
            .Include(x => x.Trip)
            .Include(x => x.Reporter)
            .Include(x => x.AssignedManager)
            .Include(x => x.AssignedByUser)
            .Include(x => x.RescueBoat)
            .Include(x => x.RescueDispatchedByUser)
            .Include(x => x.ReplacementBoat)
            .Include(x => x.ReplacementAssignedByUser)
            .Include(x => x.ReplacementTargetStation)
            .Include(x => x.Resolver);

    private sealed record AffectedFutureTripDelay(Trip Trip, int AddedDelayMinutes);

    private sealed record IncidentDelayNotificationCandidate(
        Guid BookingId,
        Guid UserId,
        string BookingCode,
        int? FromStopOrder);

    private sealed record IncidentDelayNotificationRecipient(
        Guid BookingId,
        Guid UserId,
        string BookingCode,
        DateTimeOffset? ExpectedBoardingDeparture);
}
