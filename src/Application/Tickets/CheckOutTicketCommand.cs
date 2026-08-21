using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

public sealed record CheckOutTicketCommand(
    string CodeOrToken,
    TicketScanRequestMetadata? Metadata = null) : IRequest<TicketScanDto>;

public sealed class CheckOutTicketCommandValidator : AbstractValidator<CheckOutTicketCommand>
{
    public CheckOutTicketCommandValidator()
    {
        RuleFor(x => x.CodeOrToken).NotEmpty().MaximumLength(100);
    }
}

public sealed class CheckOutTicketCommandHandler : IRequestHandler<CheckOutTicketCommand, TicketScanDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public CheckOutTicketCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<TicketScanDto> Handle(CheckOutTicketCommand request, CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(currentUser)
            && !AuthSupport.IsManager(currentUser)
            && !AuthSupport.IsStaff(currentUser))
        {
            throw new ForbiddenAccessException();
        }

        var now = _timeProvider.GetUtcNow();
        var metadata = request.Metadata ?? new TicketScanRequestMetadata();
        Domain.Entities.Ticket? ticket = null;
        TicketStatus? ticketStatusBefore = null;

        try
        {
            ticket = await TicketScanSupport.GetTicketAsync(_context, request.CodeOrToken, cancellationToken);
            ticketStatusBefore = ticket.TicketStatus;
            EnsureTicketCanBeCheckedOut(ticket);
            TicketAttendanceWindowSupport.EnsureCanCheckOutAt(ticket, now);
            await TicketStaffScanAuthorizationSupport.EnsureStaffCanOperateTicketAsync(
                _context, currentUser, ticket, now, cancellationToken);
        }
        catch (Exception exception) when (TicketScanHistorySupport.IsLoggableFailure(exception))
        {
            await TicketScanHistorySupport.SaveFailureEventAsync(
                _context,
                currentUser,
                TicketScanAction.CheckOut,
                metadata,
                now,
                request.CodeOrToken,
                ticket,
                ticketStatusBefore,
                exception,
                cancellationToken);
            throw;
        }

        ticket!.TicketStatus = TicketStatus.CheckedOut;
        ticket.CheckedOutAt = now;
        ticket.CheckedOutByUserId = currentUser.Id;
        ticket.CheckedOutByUser = currentUser;

        await CompleteBookingIfAllTicketsCheckedOutAsync(ticket, now, cancellationToken);

        await TicketScanHistorySupport.AddEventAsync(
            _context,
            currentUser,
            TicketScanAction.CheckOut,
            TicketScanResult.Success,
            metadata,
            now,
            request.CodeOrToken,
            ticket,
            ticketStatusBefore,
            ticket.TicketStatus,
            null,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        var tripId = ticket.BookingPassenger?.TripId ?? ticket.Booking.TripId;
        if (tripId.HasValue)
        {
            var notifications = await StaffTripNotificationSupport.AddPassengerScanNotificationsAsync(
                _context, tripId.Value, isCheckIn: false, now, cancellationToken);
            if (notifications.Count > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
                await NotificationSupport.PublishCreatedAsync(
                    _notificationRealtimeNotifier, notifications, cancellationToken);
            }
        }

        return await TicketScanSupport.ToDtoAsync(_context, ticket, cancellationToken, now);
    }

    private async Task CompleteBookingIfAllTicketsCheckedOutAsync(
        Domain.Entities.Ticket ticket,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var hasRemainingUsableTicket = await _context.Tickets.AnyAsync(
            x => x.BookingId == ticket.BookingId
                && x.Id != ticket.Id
                && x.TicketStatus != TicketStatus.Cancelled
                && x.TicketStatus != TicketStatus.Expired
                && x.TicketStatus != TicketStatus.CheckedOut,
            cancellationToken);

        if (!hasRemainingUsableTicket)
        {
            ticket.Booking.BookingStatus = BookingStatus.Completed;
            if (Booking.IsCharterBookingType(ticket.Booking.BookingType))
            {
                await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(
                    _context,
                    ticket.Booking,
                    cancellationToken);
            }

            // Toàn bộ vé đã check-out → khách dùng xong dịch vụ, giờ mới tích điểm.
            await PointSupport.AwardCompletionPointsAsync(
                _context,
                ticket.Booking,
                now,
                cancellationToken);
        }
    }

    private static void EnsureTicketCanBeCheckedOut(Domain.Entities.Ticket ticket)
    {
        if (ticket.TicketStatus == TicketStatus.CheckedOut || ticket.CheckedOutAt.HasValue)
        {
            throw new ValidationException([new ValidationFailure("ticket", "Ve nay da duoc check-out.")]);
        }

        // Vé hết hạn tới đây bằng HAI đường khác nhau, và quầy phải phân biệt được để giải thích
        // với khách: hoặc khách đã lên tàu rồi để quá giờ quét ở bến xuống (job dọn vé tự huỷ),
        // hoặc vé chưa từng được check-in. Trước đây cả hai đều nhận câu "chưa check-in" — sai
        // hẳn với ca thứ nhất, vì vé đó đã check-in rồi.
        if (ticket.TicketStatus == TicketStatus.Expired)
        {
            throw new ValidationException([new ValidationFailure("ticket", ticket.CheckedInAt.HasValue
                ? "Ve da qua han check-out (het gio quet tai ben khach xuong) nen bi huy tu dong."
                : "Ve da het han vi khong check-in dung gio, khong the check-out.")]);
        }

        if (ticket.TicketStatus == TicketStatus.Cancelled)
        {
            throw new ValidationException([new ValidationFailure("ticket", "Ve da bi huy, khong the check-out.")]);
        }

        if (ticket.TicketStatus != TicketStatus.CheckedIn || !ticket.CheckedInAt.HasValue)
        {
            throw new ValidationException([new ValidationFailure("ticket", "Ve chua check-in nen chua the check-out.")]);
        }

        if (ticket.Booking.BookingStatus != BookingStatus.Confirmed)
        {
            throw new ValidationException([new ValidationFailure("booking", "Booking khong san sang de check-out.")]);
        }
    }
}
