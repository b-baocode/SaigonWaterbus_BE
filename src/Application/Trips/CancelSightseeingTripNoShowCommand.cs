using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin")]
public sealed record CancelSightseeingTripNoShowCommand(
    Guid TripId,
    string? StatusNote = null) : IRequest<TripDetailDto>;

public sealed class CancelSightseeingTripNoShowCommandValidator
    : AbstractValidator<CancelSightseeingTripNoShowCommand>
{
    public CancelSightseeingTripNoShowCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.StatusNote).MaximumLength(500).When(x => x.StatusNote is not null);
    }
}

public sealed class CancelSightseeingTripNoShowCommandHandler
    : IRequestHandler<CancelSightseeingTripNoShowCommand, TripDetailDto>
{
    private const string DefaultNoShowNote = "Hủy chuyến sightseeing do khách không có mặt tại bến.";

    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public CancelSightseeingTripNoShowCommandHandler(
        IApplicationDbContext context,
        TimeProvider? timeProvider = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<TripDetailDto> Handle(
        CancelSightseeingTripNoShowCommand request,
        CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        EnsureCanCancelAsNoShow(trip);

        var hasCheckedInTicket = await _context.Set<Ticket>()
            .AnyAsync(x => x.Booking.BookingType == Booking.SeatBookingType
                        && (x.TicketStatus == TicketStatus.CheckedIn || x.TicketStatus == TicketStatus.CheckedOut)
                        && (x.BookingPassenger!.TripId == trip.Id
                            || (x.BookingPassenger!.TripId == null && x.Booking.TripId == trip.Id)),
                cancellationToken);
        if (hasCheckedInTicket)
        {
            throw new ValidationException([new ValidationFailure("ticket",
                "Không thể hủy no-show vì chuyến đã có vé check-in/check-out.")]);
        }

        var oldStatus = trip.TripStatus;
        trip.TripStatus = TripStatus.Cancelled;
        trip.StatusNote = string.IsNullOrWhiteSpace(request.StatusNote)
            ? DefaultNoShowNote
            : request.StatusNote.Trim();

        var activeTickets = await _context.Set<Ticket>()
            .Where(x => x.Booking.BookingType == Booking.SeatBookingType
                     && x.TicketStatus == TicketStatus.Active
                     && (x.BookingPassenger!.TripId == trip.Id
                         || (x.BookingPassenger!.TripId == null && x.Booking.TripId == trip.Id)))
            .ToListAsync(cancellationToken);
        foreach (var ticket in activeTickets)
        {
            ticket.TicketStatus = TicketStatus.Cancelled;
        }

        var now = _timeProvider.GetUtcNow();
        var createdNotifications = await NotificationSupport.AddTripStatusChangedNotificationsAsync(
            _context,
            trip,
            oldStatus,
            now,
            cancellationToken);
        createdNotifications = createdNotifications
            .Concat(await StaffTripNotificationSupport.AddTripStatusChangedNotificationsAsync(
                _context,
                trip,
                oldStatus,
                now,
                cancellationToken))
            .Concat(await StaffTripNotificationSupport.AddManagementTripStatusNotificationsAsync(
                _context,
                trip,
                oldStatus,
                now,
                cancellationToken))
            .ToList();

        await _context.SaveChangesAsync(cancellationToken);
        await NotificationSupport.PublishCreatedAsync(
            _notificationRealtimeNotifier, createdNotifications, cancellationToken);

        return UpdateTripStatusCommandHandler.ToDetailDto(
            trip,
            sourceBooking: null,
            stops: TripStopScheduleSupport.BuildStopDtos(trip),
            totalPassengerCount: activeTickets.Count);
    }

    private static void EnsureCanCancelAsNoShow(Trip trip)
    {
        if (!string.Equals(trip.Route.RouteType, RouteTypes.SightseeingLoop, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure("trip",
                "Chỉ chuyến sightseeing mới được hủy theo lý do khách không tới.")]);
        }

        if (trip.TripStatus is TripStatus.Completed or TripStatus.InProgress)
        {
            throw new ValidationException([new ValidationFailure(nameof(trip.TripStatus),
                "Chuyến đã chạy hoặc đã hoàn thành nên không thể hủy no-show.")]);
        }

        if (trip.TripStops.Any(x => x.ActualDepartureTime.HasValue
            || string.Equals(x.StopStatus, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException([new ValidationFailure(nameof(trip.TripStatus),
                "Tàu đã rời bến nên không thể hủy no-show.")]);
        }

        if (trip.TripStatus == TripStatus.Cancelled)
        {
            throw new ValidationException([new ValidationFailure(nameof(trip.TripStatus),
                "Chuyến đã bị hủy.")]);
        }
    }
}
