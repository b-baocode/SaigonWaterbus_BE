using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record AddCharterBookingPassengersCommand(
    Guid BookingId,
    IReadOnlyList<CharterBookingPassengerRequest> Passengers)
    : IRequest<UpdateCharterBookingPassengersResult>;

public sealed class AddCharterBookingPassengersCommandValidator
    : AbstractValidator<AddCharterBookingPassengersCommand>
{
    public AddCharterBookingPassengersCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.Passengers)
            .NotNull()
            .NotEmpty()
            .WithMessage("Danh sách hành khách cần thêm là bắt buộc.");
        RuleForEach(x => x.Passengers).SetValidator(new CharterBookingPassengerRequestValidator());
    }
}

public sealed class AddCharterBookingPassengersCommandHandler
    : IRequestHandler<AddCharterBookingPassengersCommand, UpdateCharterBookingPassengersResult>
{
    private const string PaidBookingPaymentStatus = BookingPaymentStatusExtensions.PaidValue;

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public AddCharterBookingPassengersCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<UpdateCharterBookingPassengersResult> Handle(
        AddCharterBookingPassengersCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.Passengers)
            .Include(x => x.Payments)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .Include(x => x.Boat)
            .Include(x => x.CharterBoats)
                .ThenInclude(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (booking.UserId != userId)
        {
            throw new NotFoundException("Charter booking not found.");
        }

        if (booking.BookingStatus != BookingStatus.Confirmed)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Chỉ thêm hành khách khi charter booking đã được xác nhận.")]);
        }

        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PaymentStatus),
                "Chỉ thêm hành khách sau khi charter booking đã thanh toán đủ.")]);
        }

        if (booking.Tickets.Any(x => x.TicketStatus is TicketStatus.CheckedIn or TicketStatus.CheckedOut))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Passengers),
                "Không thể thêm hành khách khi đã có vé check-in hoặc check-out.")]);
        }

        var now = _timeProvider.GetUtcNow();
        CharterBookingPassengerSupport.EnsureManifestCanBeUpdatedBeforeCutoff(
            booking,
            now,
            nameof(request.Passengers));
        CharterBookingPassengerSupport.EnsurePassengerAddRequestCountAvailable(
            booking,
            nameof(request.Passengers));
        var countedPassengerCount = booking.Passengers.Count(x =>
            !CharterBookingPassengerSupport.IsRejected(x));
        CharterBookingPassengerSupport.EnsurePassengerCountDoesNotExceedSelectedBoatCapacity(
            booking,
            countedPassengerCount + request.Passengers.Count,
            nameof(request.Passengers));

        // Đây là luồng bổ sung hành khách, không phải luồng báo giá charter ban đầu.
        booking.BookingStatus = BookingStatus.PendingApproval;

        var requestBatchId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var newPassengers = request.Passengers
            .Select((x, index) => CharterBookingPassengerSupport.ToEntity(
                booking.Id,
                x,
                today,
                null,
                $"passengers[{index}].birthYear",
                $"passengers[{index}].fullName"))
            .ToList();

        foreach (var passenger in newPassengers)
        {
            passenger.ApprovalStatus = CharterBookingPassengerSupport.ApprovalStatusPending;
            passenger.RequestBatchId = requestBatchId;
            passenger.RequestedAt = now;
            passenger.RequestedByUserId = userId;
            booking.Passengers.Add(passenger);
        }
        _context.Set<BookingPassenger>().AddRange(newPassengers);

        await _context.SaveChangesAsync(cancellationToken);

        var addedNotifications = await NotificationSupport.AddCharterPassengerAddRequestedNotificationsAsync(
            _context,
            booking,
            newPassengers.Count,
            now,
            cancellationToken);
        if (addedNotifications.Count > 0)
        {
            await NotificationSupport.PublishCreatedAsync(
                _notificationRealtimeNotifier,
                addedNotifications,
                cancellationToken);
        }

        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "PassengerAddRequested",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                now),
            cancellationToken);

        return CharterBookingPassengerResultSupport.ToUpdateResult(booking);
    }
}
