using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin,Manager,Staff")]
public sealed record UpdateTripStatusCommand(
    Guid TripId,
    TripStatus TripStatus,
    string? StatusNote) : IRequest<TripDetailDto>;

public sealed class UpdateTripStatusCommandValidator : AbstractValidator<UpdateTripStatusCommand>
{
    public UpdateTripStatusCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
    }
}

public sealed class UpdateTripStatusCommandHandler : IRequestHandler<UpdateTripStatusCommand, TripDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public UpdateTripStatusCommandHandler(
        IApplicationDbContext context,
        TimeProvider? timeProvider = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<TripDetailDto> Handle(UpdateTripStatusCommand request, CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .Include(t => t.Boat)
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.Station)
            .SingleOrDefaultAsync(t => t.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        var oldStatus = trip.TripStatus;
        trip.TripStatus = request.TripStatus;
        trip.StatusNote = request.StatusNote;

        var now = _timeProvider.GetUtcNow();
        var createdNotifications = new List<Notification>();
        createdNotifications.AddRange(await NotificationSupport.AddTripStatusChangedNotificationsAsync(
            _context,
            trip,
            oldStatus,
            now,
            cancellationToken));
        createdNotifications.AddRange(await NotificationSupport.AddTripCompletedReviewInviteNotificationsAsync(
            _context,
            trip,
            oldStatus,
            now,
            cancellationToken));

        // Dịch vụ đã dùng xong → mới tích điểm (booking khứ hồi chờ đủ 2 chiều).
        await PointSupport.AwardCompletionPointsForCompletedTripAsync(
            _context,
            trip,
            oldStatus,
            now,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await NotificationSupport.PublishCreatedAsync(
            _notificationRealtimeNotifier, createdNotifications, cancellationToken);

        var sourceBooking = await LoadSourceBookingAsync(trip, cancellationToken);
        return ToDetailDto(trip, sourceBooking);
    }

    internal static TripDetailDto ToDetailDto(
        Trip trip,
        Booking? sourceBooking = null,
        IReadOnlyList<TripStopDto>? stops = null,
        IReadOnlyList<TripStaffAssignmentDto>? onBoardStaff = null,
        int totalPassengerCount = 0) => new(
        trip.Id, trip.TripCode,
        trip.Route.Id, trip.Route.RouteName,
        trip.Route.RouteType,
        DistanceFareSupport.UsesDistanceFare(trip.TripType, trip.Route.RouteType),
        trip.DepartureTime, trip.ArrivalTime,
        trip.CapacitySnapshot, trip.TripStatus.ToString(), trip.StatusNote,
        stops ?? TripStopScheduleSupport.BuildStopDtos(trip),
        trip.TripType,
        trip.SourceBookingId,
        sourceBooking?.BookingCode,
        TripMediaSupport.ToBoatDto(trip.Boat, trip.CapacitySnapshot),
        onBoardStaff ?? [],
        totalPassengerCount,
        trip.Route.RouteCode,
        TripMediaSupport.ResolveFromStation(trip),
        TripMediaSupport.ResolveToStation(trip),
        stops?.Count ?? (trip.TripStops.Count > 0 ? trip.TripStops.Count : trip.Route.RouteStops.Count),
        TripDelaySupport.ToDelayInfoDto(trip));

    private async Task<Booking?> LoadSourceBookingAsync(Trip trip, CancellationToken cancellationToken)
    {
        if (!trip.SourceBookingId.HasValue)
        {
            return null;
        }

        return await _context.Set<Booking>()
            .Include(x => x.ItineraryStops)
            .SingleOrDefaultAsync(x => x.Id == trip.SourceBookingId.Value, cancellationToken);
    }

}
