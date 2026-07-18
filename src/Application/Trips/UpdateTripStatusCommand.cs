using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.Notifications;
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

        var createdNotifications = await NotificationSupport.AddTripStatusChangedNotificationsAsync(
            _context,
            trip,
            oldStatus,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await NotificationSupport.PublishCreatedAsync(
            _notificationRealtimeNotifier, createdNotifications, cancellationToken);

        var sourceBooking = await LoadSourceBookingAsync(trip, cancellationToken);
        return ToDetailDto(trip, sourceBooking);
    }

    internal static TripDetailDto ToDetailDto(Trip trip, Booking? sourceBooking = null) => new(
        trip.Id, trip.TripCode,
        trip.Route.Id, trip.Route.RouteName,
        trip.Route.RouteType,
        DistanceFareSupport.UsesDistanceFare(trip.TripType, trip.Route.RouteType),
        trip.DepartureTime, trip.ArrivalTime,
        trip.CapacitySnapshot, trip.TripStatus.ToString(), trip.StatusNote,
        TripStopScheduleSupport.BuildStopDtos(trip),
        trip.TripType,
        trip.SourceBookingId,
        sourceBooking?.BookingCode);

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
