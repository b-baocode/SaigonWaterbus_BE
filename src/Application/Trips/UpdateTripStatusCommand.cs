using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.Incidents;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin,Staff")]
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
        var sourceBooking = await LoadSourceBookingAsync(trip, cancellationToken);

        // Tự động cập nhật trạng thái vé khi trip kết thúc
        await UpdateTicketStatusesForTripAsync(_context, trip, oldStatus, request.TripStatus, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var createdNotifications = new List<Notification>();
        createdNotifications.AddRange(await NotificationSupport.AddTripStatusChangedNotificationsAsync(
            _context,
            trip,
            oldStatus,
            now,
            cancellationToken));
        createdNotifications.AddRange(await StaffTripNotificationSupport.AddTripStatusChangedNotificationsAsync(
            _context,
            trip,
            oldStatus,
            now,
            cancellationToken));
        createdNotifications.AddRange(await StaffTripNotificationSupport.AddManagementTripStatusNotificationsAsync(
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

        // Trip Completed → booking gốc tự động sang Hoàn tất (xử lý cả 1 chiều/khứ hồi).
        if (request.TripStatus == TripStatus.Completed && oldStatus != TripStatus.Completed)
        {
            var completionChanged = await CharterBookingTripSupport.CompleteLinkedBookingAsync(
                _context,
                trip,
                now,
                cancellationToken);
            if (completionChanged && sourceBooking is not null)
            {
                createdNotifications.AddRange(await NotificationSupport.AddBookingCompletedNotificationsAsync(
                    _context,
                    sourceBooking,
                    now,
                    cancellationToken));
            }
        }

        // Trip Cancelled → booking gốc tự động sang Hủy (đã hủy thì cũng phải hủy).
        else if (request.TripStatus == TripStatus.Cancelled && oldStatus != TripStatus.Cancelled)
        {
            var cancellationChanged = await CharterBookingTripSupport.CancelLinkedBookingAsync(
                _context,
                trip,
                now,
                cancellationToken);
            if (cancellationChanged && sourceBooking is not null)
            {
                createdNotifications.AddRange(await NotificationSupport.AddCharterBookingCancelledNotificationsAsync(
                    _context,
                    sourceBooking,
                    now,
                    cancellationToken));
            }
        }

        if (ShouldDeactivateCharterRoute(trip, oldStatus, sourceBooking))
        {
            await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(
                _context,
                sourceBooking!,
                cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await NotificationSupport.PublishCreatedAsync(
            _notificationRealtimeNotifier, createdNotifications, cancellationToken);

        var charterPassengers = sourceBooking is null
            ? null
            : CharterTripPassengerMapper.FromBooking(sourceBooking);

        return ToDetailDto(trip, sourceBooking, charterPassengers: charterPassengers);
    }

    internal static TripDetailDto ToDetailDto(
        Trip trip,
        Booking? sourceBooking = null,
        IReadOnlyList<TripStopDto>? stops = null,
        IReadOnlyList<TripStaffAssignmentDto>? onBoardStaff = null,
        int totalPassengerCount = 0,
        int onboardPassengerCount = 0,
        TripIncidentInfoDto? incidentInfo = null,
        IReadOnlyList<CharterTripPassengerInfoDto>? charterPassengers = null) => new(
        trip.Id, trip.TripCode,
        trip.Route.Id, trip.Route.RouteName,
        trip.Route.RouteType,
        DistanceFareSupport.UsesDistanceFare(trip.TripType, trip.Route.RouteType),
        trip.DepartureTime, trip.ArrivalTime,
        trip.CapacitySnapshot,
        trip.TripStatus.ToString(),
        trip.StatusNote,
        stops ?? TripStopScheduleSupport.BuildStopDtos(trip),
        trip.TripType,
        trip.SourceBookingId,
        sourceBooking?.BookingCode,
        TripMediaSupport.ToBoatDto(trip.Boat, trip.CapacitySnapshot),
        onBoardStaff ?? [],
        totalPassengerCount,
        onboardPassengerCount,
        trip.Route.RouteCode,
        TripMediaSupport.ResolveFromStation(trip),
        TripMediaSupport.ResolveToStation(trip),
        stops?.Count ?? (trip.TripStops.Count > 0 ? trip.TripStops.Count : trip.Route.RouteStops.Count),
        TripDelaySupport.ToDelayInfoDto(trip),
        trip.AdjustedDepartureTime,
        trip.AdjustedArrivalTime,
        incidentInfo,
        OperatingStatus: ResolveOperatingStatus(trip, incidentInfo),
        CharterInfo: sourceBooking is null ? null : ToCharterInfoDto(sourceBooking, charterPassengers));

    private static CharterTripInfoDto ToCharterInfoDto(
        Booking booking,
        IReadOnlyList<CharterTripPassengerInfoDto>? passengers = null) => new(
        booking.Id,
        booking.BookingCode,
        booking.BookingStatus.ToString(),
        booking.ContactName,
        booking.ContactPhone,
        booking.ContactEmail,
        booking.PassengerCount,
        booking.AdultCount,
        booking.ChildCount,
        booking.SpecialRequests,
        booking.BoatRequirements,
        booking.PreferredSeatSetupType?.ToString(),
        booking.RequestedBoatCount,
        booking.RentalUnit?.ToString(),
        booking.DurationValue,
        booking.CharterBookingQrToken,
        booking.CharterRouteId,
        booking.ReturnTripId,
        passengers);

    private static string ResolveOperatingStatus(Trip trip, TripIncidentInfoDto? incidentInfo) =>
        incidentInfo is not null
        && string.Equals(incidentInfo.ResolutionStatus, IncidentSupport.OpenStatus, StringComparison.OrdinalIgnoreCase)
            ? incidentInfo.OperatingStatus ?? OperatingStatusSupport.ForTrip(trip)
            : OperatingStatusSupport.ForTrip(trip);

    private async Task<Booking?> LoadSourceBookingAsync(Trip trip, CancellationToken cancellationToken)
    {
        if (!trip.SourceBookingId.HasValue)
        {
            return null;
        }

        return await _context.Set<Booking>()
            .Include(x => x.ItineraryStops)
            .Include(x => x.CharterRoute)
            .Include(x => x.Passengers)
                .ThenInclude(p => p.Tickets)
            .SingleOrDefaultAsync(x => x.Id == trip.SourceBookingId.Value, cancellationToken);
    }

    private static bool ShouldDeactivateCharterRoute(
        Trip trip,
        TripStatus oldStatus,
        Booking? sourceBooking) =>
        sourceBooking is not null
        && trip.TripStatus != oldStatus
        && trip.TripStatus == TripStatus.Completed
        && (trip.TripType == TripTypes.Charter
            || trip.Route.RouteType == RouteTypes.Charter
            || trip.Route.RouteType == RouteTypes.CharterReference);

    private async Task UpdateTicketStatusesForTripAsync(
        IApplicationDbContext context,
        Trip trip,
        TripStatus oldStatus,
        TripStatus newStatus,
        CancellationToken cancellationToken)
    {
        // Chỉ xử lý khi trip chuyển sang Completed hoặc Cancelled
        if (newStatus != TripStatus.Completed && newStatus != TripStatus.Cancelled)
        {
            return;
        }

        // Load tickets theo 2 chiều liên kết:
        // 1. Booking.TripId = trip.Id (ticket thường - khách mua vé ghép từng segment)
        // 2. Booking.Id = trip.SourceBookingId (charter booking - 1 booking tạo cả trip)
        var relatedBookingIds = await context.Set<Booking>()
            .Where(b => b.TripId == trip.Id
                || (trip.SourceBookingId.HasValue && b.Id == trip.SourceBookingId.Value))
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        if (relatedBookingIds.Count == 0)
        {
            return;
        }

        var tickets = await context.Set<Booking>()
            .Where(b => relatedBookingIds.Contains(b.Id))
            .SelectMany(b => b.Tickets)
            .ToListAsync(cancellationToken);

        foreach (var ticket in tickets)
        {
            switch (ticket.TicketStatus)
            {
                case TicketStatus.Active:
                case TicketStatus.CheckedIn:
                    // Chưa check-in hoặc đã check-in nhưng chưa check-out → hết hạn
                    ticket.TicketStatus = TicketStatus.Expired;
                    break;

                case TicketStatus.CheckedOut:
                    // Đã check-in và check-out → đã sử dụng
                    break;

                case TicketStatus.Cancelled:
                case TicketStatus.Expired:
                    // Giữ nguyên
                    break;
            }
        }
    }

}
