using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

/// <summary>Admin override cho lịch charter chưa kết thúc; đồng bộ các trip charter đã được sinh.</summary>
public sealed record RescheduleCharterBookingCommand(
    Guid BookingId,
    DateOnly DepartureDate,
    TimeOnly StartTime) : IRequest<CharterBookingDetailDto>;

public sealed class RescheduleCharterBookingCommandValidator
    : AbstractValidator<RescheduleCharterBookingCommand>
{
    public RescheduleCharterBookingCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.DepartureDate).NotEqual(default(DateOnly));
        RuleFor(x => x.StartTime)
            .Must(CharterBookingTripSupport.IsWithinOperatingStartWindow)
            .WithMessage("Giờ bắt đầu charter phải nằm trong khung 07:00 đến trước 23:00.");
    }
}

public sealed class RescheduleCharterBookingCommandHandler
    : IRequestHandler<RescheduleCharterBookingCommand, CharterBookingDetailDto>
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public RescheduleCharterBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<CharterBookingDetailDto> Handle(
        RescheduleCharterBookingCommand request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildDetailQuery(_context)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Expired or BookingStatus.Completed)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                "Không thể đổi lịch charter booking đã hủy, hết hạn hoặc hoàn tất.")]);
        }

        var newDeparture = CharterBookingTripSupport.ResolveVietnamTimeUtc(
            request.DepartureDate,
            request.StartTime);
        if (newDeparture <= _timeProvider.GetUtcNow())
        {
            throw new ValidationException([new ValidationFailure(nameof(request.DepartureDate),
                "Ngày và giờ khởi hành mới phải ở tương lai.")]);
        }

        var trips = await _context.Set<Trip>()
            .Include(x => x.TripStops)
            .Where(x => x.SourceBookingId == booking.Id)
            .ToListAsync(cancellationToken);
        if (trips.Any(x => x.TripStatus != TripStatus.Scheduled))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                "Chỉ được đổi lịch khi tất cả charter trip còn ở trạng thái Scheduled.")]);
        }

        if (booking.Tickets.Any(x =>
                x.TicketStatus is TicketStatus.CheckedIn or TicketStatus.CheckedOut
                || x.CheckedInAt.HasValue
                || x.CheckedOutAt.HasValue))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                "Không thể đổi lịch khi booking đã có hành khách check-in hoặc check-out.")]);
        }

        var fallbackArrival = ResolveFallbackArrivalTimeUtc(request, booking, newDeparture);
        var proposedTripSchedules = BuildProposedTripSchedules(
            trips,
            newDeparture,
            fallbackArrival);
        var proposedSchedules = BuildProposedSchedules(
            booking,
            proposedTripSchedules,
            newDeparture,
            fallbackArrival);
        EnsureWithinOperatingHours(request, booking, proposedSchedules);
        await EnsureBoatsAreAvailableAsync(
            booking,
            trips,
            proposedSchedules,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        booking.DepartureDate = request.DepartureDate;
        booking.StartTime = request.StartTime;

        var boatOrdersByTripId = ResolveBoatOrdersByTripId(booking, proposedTripSchedules);
        var proposedTripStopSchedules = proposedTripSchedules
            .SelectMany(x => BuildProposedTripStopSchedules(x.Trip, x.DepartureTime))
            .ToArray();
        foreach (var proposedTripSchedule in proposedTripSchedules)
        {
            var trip = proposedTripSchedule.Trip;
            ReanchorTrip(trip, proposedTripSchedule.DepartureTime, proposedTripSchedule.ArrivalTime);
            trip.OperatingDate = request.DepartureDate;
            trip.TripCode = CharterBookingTripSupport.BuildTripCode(
                booking,
                boatOrdersByTripId[trip.Id]);
        }

        await _context.ExecuteInTransactionAsync(async ct =>
        {
            // PostgreSQL có trigger cascade trip -> trip_stops. Lưu trip trước, sau đó ghi đè
            // schedule chuẩn để cả stop đã sai từ trước cũng được neo lại đúng ngày/giờ mới.
            await _context.SaveChangesAsync(ct);
            if (proposedTripStopSchedules.Length > 0)
            {
                ApplyProposedTripStopSchedules(proposedTripStopSchedules);
                _context.Set<TripStop>().UpdateRange(proposedTripStopSchedules.Select(x => x.Stop));
                await _context.SaveChangesAsync(ct);
            }
        }, cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "RescheduledByAdmin",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                _timeProvider.GetUtcNow()),
            cancellationToken);

        var updatedBooking = await CharterBookingQuerySupport.BuildDetailQuery(_context)
            .SingleAsync(x => x.Id == booking.Id, cancellationToken);
        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context, updatedBooking, cancellationToken);
        return CharterBookingQuerySupport.ToDetailDto(updatedBooking, relatedRoutes);
    }

    private static DateTimeOffset ResolveFallbackArrivalTimeUtc(
        RescheduleCharterBookingCommand request,
        Booking booking,
        DateTimeOffset newDeparture)
    {
        var durationValue = CharterBookingRoutePricingSupport.ResolveRequestedDurationValue(booking);
        if (CharterBookingRoutePricingSupport.ResolveRentalUnit(booking) == BoatRentalUnit.Day)
        {
            return CharterBookingTripSupport.ResolveVietnamTimeUtc(
                request.DepartureDate.AddDays(durationValue - 1),
                CharterBookingTripSupport.OperatingDayEndTime);
        }

        return newDeparture.AddHours(durationValue);
    }

    private static IReadOnlyList<ProposedTripSchedule> BuildProposedTripSchedules(
        IReadOnlyCollection<Trip> trips,
        DateTimeOffset newDeparture,
        DateTimeOffset fallbackArrival)
    {
        var fallbackDuration = fallbackArrival - newDeparture;
        return trips
            .Select(trip =>
            {
                // Mỗi linked trip là một tàu chạy cùng charter, nên cùng khởi hành đúng giờ booking.
                var departure = newDeparture;
                var currentDuration = trip.ArrivalTime - trip.DepartureTime;
                var duration = currentDuration > TimeSpan.Zero ? currentDuration : fallbackDuration;
                return new ProposedTripSchedule(trip, departure, departure.Add(duration));
            })
            .ToList();
    }

    private static IReadOnlyList<ProposedSchedule> BuildProposedSchedules(
        Booking booking,
        IReadOnlyCollection<ProposedTripSchedule> tripSchedules,
        DateTimeOffset newDeparture,
        DateTimeOffset fallbackArrival)
    {
        var schedules = tripSchedules
            .Select(x => new ProposedSchedule(
                x.Trip.BoatId ?? Guid.Empty,
                x.DepartureTime,
                x.ArrivalTime))
            .ToList();
        var scheduledBoatIds = schedules
            .Where(x => x.BoatId != Guid.Empty)
            .Select(x => x.BoatId)
            .ToHashSet();
        foreach (var boatId in CharterBookingBoatSelectionSupport.ResolveSelectedBoatIds(booking)
                     .Where(x => !scheduledBoatIds.Contains(x)))
        {
            schedules.Add(new ProposedSchedule(boatId, newDeparture, fallbackArrival));
        }

        if (schedules.Count == 0)
        {
            schedules.Add(new ProposedSchedule(Guid.Empty, newDeparture, fallbackArrival));
        }

        return schedules;
    }

    private static IReadOnlyDictionary<Guid, int> ResolveBoatOrdersByTripId(
        Booking booking,
        IReadOnlyCollection<ProposedTripSchedule> tripSchedules)
    {
        var result = booking.CharterBoats
            .Where(x => x.TripId.HasValue)
            .GroupBy(x => x.TripId!.Value)
            .ToDictionary(x => x.Key, x => x.OrderBy(boat => boat.BoatOrder).First().BoatOrder);
        var usedOrders = result.Values.ToHashSet();
        var nextOrder = 1;
        foreach (var schedule in tripSchedules
                     .OrderByDescending(x => booking.TripId == x.Trip.Id)
                     .ThenBy(x => x.Trip.Id))
        {
            if (result.ContainsKey(schedule.Trip.Id))
            {
                continue;
            }

            while (usedOrders.Contains(nextOrder))
            {
                nextOrder++;
            }

            result[schedule.Trip.Id] = nextOrder;
            usedOrders.Add(nextOrder);
        }

        return result;
    }

    private static IReadOnlyList<ProposedTripStopSchedule> BuildProposedTripStopSchedules(
        Trip trip,
        DateTimeOffset newDeparture)
    {
        var orderedStops = trip.TripStops.OrderBy(x => x.StopOrder).ToArray();
        var oldStopAnchor = orderedStops
            .Select(x => x.PlannedDepartureTime ?? x.PlannedArrivalTime)
            .FirstOrDefault(x => x.HasValue)
            ?? trip.DepartureTime;

        return orderedStops
            .Select(stop =>
            {
                var newPlannedArrival = Reanchor(stop.PlannedArrivalTime, oldStopAnchor, newDeparture);
                var newPlannedDeparture = Reanchor(stop.PlannedDepartureTime, oldStopAnchor, newDeparture);
                return new ProposedTripStopSchedule(
                    stop,
                    newPlannedArrival,
                    newPlannedDeparture,
                    ReanchorAdjusted(
                        stop.AdjustedArrivalTime,
                        stop.PlannedArrivalTime,
                        newPlannedArrival,
                        oldStopAnchor,
                        newDeparture),
                    ReanchorAdjusted(
                        stop.AdjustedDepartureTime,
                        stop.PlannedDepartureTime,
                        newPlannedDeparture,
                        oldStopAnchor,
                        newDeparture));
            })
            .ToArray();
    }

    private static void ApplyProposedTripStopSchedules(
        IReadOnlyCollection<ProposedTripStopSchedule> schedules)
    {
        foreach (var schedule in schedules)
        {
            schedule.Stop.PlannedArrivalTime = schedule.PlannedArrivalTime;
            schedule.Stop.PlannedDepartureTime = schedule.PlannedDepartureTime;
            schedule.Stop.AdjustedArrivalTime = schedule.AdjustedArrivalTime;
            schedule.Stop.AdjustedDepartureTime = schedule.AdjustedDepartureTime;
        }
    }

    private static void ReanchorTrip(
        Trip trip,
        DateTimeOffset newDeparture,
        DateTimeOffset newArrival)
    {
        var oldDeparture = trip.DepartureTime;
        var oldArrival = trip.ArrivalTime;

        trip.AdjustedDepartureTime = ReanchorAdjusted(
            trip.AdjustedDepartureTime,
            oldDeparture,
            newDeparture,
            oldDeparture,
            newDeparture);
        trip.AdjustedArrivalTime = ReanchorAdjusted(
            trip.AdjustedArrivalTime,
            oldArrival,
            newArrival,
            oldArrival,
            newArrival);
        trip.DepartureTime = newDeparture;
        trip.ArrivalTime = newArrival;
    }

    private static DateTimeOffset? Reanchor(
        DateTimeOffset? value,
        DateTimeOffset oldAnchor,
        DateTimeOffset newAnchor) =>
        value.HasValue ? newAnchor.Add(value.Value - oldAnchor) : null;

    private static DateTimeOffset? ReanchorAdjusted(
        DateTimeOffset? adjusted,
        DateTimeOffset? planned,
        DateTimeOffset? newPlanned,
        DateTimeOffset oldAnchor,
        DateTimeOffset newAnchor)
    {
        if (!adjusted.HasValue)
        {
            return null;
        }

        return planned.HasValue && newPlanned.HasValue
            ? newPlanned.Value.Add(adjusted.Value - planned.Value)
            : newAnchor.Add(adjusted.Value - oldAnchor);
    }

    private static void EnsureWithinOperatingHours(
        RescheduleCharterBookingCommand request,
        Booking booking,
        IReadOnlyCollection<ProposedSchedule> schedules)
    {
        if (!CharterBookingTripSupport.IsWithinOperatingStartWindow(request.StartTime))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.StartTime),
                "Giờ bắt đầu charter phải nằm trong khung 07:00 đến trước 23:00.")]);
        }

        var rentalUnit = CharterBookingRoutePricingSupport.ResolveRentalUnit(booking);
        var invalidEnd = schedules.FirstOrDefault(schedule =>
        {
            var localArrival = schedule.ArrivalTime.ToOffset(VietnamOffset);
            return TimeOnly.FromDateTime(localArrival.DateTime) > CharterBookingTripSupport.OperatingDayEndTime
                || (rentalUnit == BoatRentalUnit.Hour
                    && DateOnly.FromDateTime(localArrival.DateTime) != request.DepartureDate);
        });
        if (invalidEnd is not null)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.StartTime),
                "Charter phải kết thúc chậm nhất lúc 23:00 trong ngày vận hành.")]);
        }
    }

    private async Task EnsureBoatsAreAvailableAsync(
        Booking booking,
        IReadOnlyCollection<Trip> linkedTrips,
        IReadOnlyCollection<ProposedSchedule> proposedSchedules,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (proposedSchedules.Count == 0)
        {
            return;
        }

        var boatIds = proposedSchedules
            .Where(x => x.BoatId != Guid.Empty)
            .Select(x => x.BoatId)
            .Distinct()
            .ToArray();
        if (boatIds.Length == 0)
        {
            return;
        }
        var linkedTripIds = linkedTrips.Select(x => x.Id).ToArray();
        var windowStart = proposedSchedules.Min(x => x.DepartureTime)
            .Subtract(TripScheduleSupport.BoatTurnaroundBuffer);
        var windowEnd = proposedSchedules.Max(x => x.ArrivalTime)
            .Add(TripScheduleSupport.BoatTurnaroundBuffer);
        var conflictingTrips = await _context.Set<Trip>()
            .AsNoTracking()
            .Where(x => x.BoatId.HasValue
                && boatIds.Contains(x.BoatId.Value)
                && !linkedTripIds.Contains(x.Id)
                && x.SourceBookingId != booking.Id
                && x.TripStatus != TripStatus.Cancelled
                && x.DepartureTime < windowEnd
                && windowStart < x.ArrivalTime)
            .OrderBy(x => x.DepartureTime)
            .Select(x => new { x.BoatId, x.TripCode, x.DepartureTime, x.ArrivalTime })
            .ToListAsync(cancellationToken);

        foreach (var schedule in proposedSchedules)
        {
            var conflict = conflictingTrips.FirstOrDefault(x =>
                x.BoatId == schedule.BoatId
                && TripScheduleSupport.ConflictsWithBuffer(
                    x.DepartureTime,
                    x.ArrivalTime,
                    schedule.DepartureTime,
                    schedule.ArrivalTime));
            if (conflict is not null)
            {
                throw new ValidationException([new ValidationFailure(nameof(RescheduleCharterBookingCommand.StartTime),
                    "Tàu đã có chuyến trong khung giờ mới: "
                    + TripScheduleSupport.BuildConflictMessage(
                        conflict.TripCode,
                        conflict.DepartureTime,
                        conflict.ArrivalTime))]);
            }
        }

        var conflictingBookings = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .AsNoTracking()
            .Include(x => x.CharterBoats)
            .Where(x => x.Id != booking.Id
                && (x.BookingStatus == BookingStatus.Confirmed
                    || (x.BookingStatus == BookingStatus.Quoted
                        && x.HoldExpiresAt.HasValue
                        && x.HoldExpiresAt > now))
                && ((x.BoatId.HasValue && boatIds.Contains(x.BoatId.Value))
                    || x.CharterBoats.Any(cb => boatIds.Contains(cb.BoatId))))
            .ToListAsync(cancellationToken);

        foreach (var conflict in conflictingBookings)
        {
            var conflictBoatIds = CharterBookingBoatSelectionSupport.ResolveSelectedBoatIds(conflict);
            var conflictWindow = CharterBookingTripSupport.ResolveRentalWindowUtc(conflict);
            var schedule = proposedSchedules.FirstOrDefault(x =>
                conflictBoatIds.Contains(x.BoatId)
                && CharterBookingTripSupport.HasScheduleOverlap(
                    x.DepartureTime.Subtract(TripScheduleSupport.BoatTurnaroundBuffer),
                    x.ArrivalTime.Add(TripScheduleSupport.BoatTurnaroundBuffer),
                    conflictWindow.DepartureTime,
                    conflictWindow.ArrivalTime));
            if (schedule is not null)
            {
                throw new ValidationException([new ValidationFailure(nameof(RescheduleCharterBookingCommand.StartTime),
                    $"Tàu đã được giữ hoặc xác nhận cho charter booking {conflict.BookingCode} "
                    + $"trong khung {CharterBookingTripSupport.FormatVietnamWindow(conflictWindow)}.")]);
            }
        }
    }

    private sealed record ProposedSchedule(
        Guid BoatId,
        DateTimeOffset DepartureTime,
        DateTimeOffset ArrivalTime);

    private sealed record ProposedTripSchedule(
        Trip Trip,
        DateTimeOffset DepartureTime,
        DateTimeOffset ArrivalTime);

    private sealed record ProposedTripStopSchedule(
        TripStop Stop,
        DateTimeOffset? PlannedArrivalTime,
        DateTimeOffset? PlannedDepartureTime,
        DateTimeOffset? AdjustedArrivalTime,
        DateTimeOffset? AdjustedDepartureTime);
}
