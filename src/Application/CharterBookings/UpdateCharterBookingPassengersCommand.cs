using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record UpdateCharterBookingPassengersCommand(
    Guid BookingId,
    IReadOnlyList<CharterBookingPassengerRequest> Passengers)
    : IRequest<UpdateCharterBookingPassengersResult>;

public sealed class UpdateCharterBookingPassengersCommandValidator
    : AbstractValidator<UpdateCharterBookingPassengersCommand>
{
    public UpdateCharterBookingPassengersCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.Passengers).NotNull();
        RuleForEach(x => x.Passengers).SetValidator(new CharterBookingPassengerRequestValidator());
    }
}

public sealed class UpdateCharterBookingPassengersCommandHandler
    : IRequestHandler<UpdateCharterBookingPassengersCommand, UpdateCharterBookingPassengersResult>
{
    private const string PaidBookingPaymentStatus = "Paid";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly ICharterBookingTicketPdfRenderer _ticketPdfRenderer;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public UpdateCharterBookingPassengersCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IPaymentNotificationSender paymentNotificationSender,
        ICharterBookingTicketPdfRenderer ticketPdfRenderer,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentNotificationSender = paymentNotificationSender;
        _ticketPdfRenderer = ticketPdfRenderer;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<UpdateCharterBookingPassengersResult> Handle(
        UpdateCharterBookingPassengersCommand request,
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

        if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.Refunded)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể cập nhật danh sách hành khách cho booking đã hủy hoặc đã hoàn tất.")]);
        }

        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PaymentStatus),
                "Chỉ nhập danh sách hành khách sau khi charter booking đã thanh toán đủ.")]);
        }

        var now = _timeProvider.GetUtcNow();
        CharterBookingPassengerSupport.EnsureManifestCanBeUpdatedBeforeCutoff(
            booking,
            now,
            nameof(request.Passengers));

        var requestedPassengers = NormalizePassengerRequests(booking, request.Passengers);
        CharterBookingPassengerSupport.EnsurePassengerCountDoesNotExceedSelectedBoatCapacity(
            booking,
            requestedPassengers.Count,
            nameof(request.Passengers));

        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var passengers = requestedPassengers
            .Select((x, index) => CharterBookingPassengerSupport.ToEntity(
                booking.Id,
                x,
                today,
                ResolveInferredPassengerType(booking, requestedPassengers, x),
                $"passengers[{index}].dateOfBirth",
                $"passengers[{index}].fullName"))
            .ToList();
        CharterBookingTicketSupport.CancelTicketsBeforeReplacingPassengers(booking);
        _context.Set<BookingPassenger>().RemoveRange(booking.Passengers);
        booking.Passengers = passengers;
        booking.PassengerCount = passengers.Count;
        booking.AdultCount = CharterBookingPassengerSupport.CountAdults(passengers);
        booking.ChildCount = CharterBookingPassengerSupport.CountChildren(passengers);

        var ticketResult = await CharterBookingTicketSupport.EnsurePassengerTicketsAsync(
            _context,
            booking,
            _timeProvider,
            cancellationToken);
        var additionalInsuranceAmount = CharterBookingInsuranceSupport.ApplyPassengerQuantityIncrease(
            booking,
            passengers.Count,
            now);

        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "PassengersUpdated",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                _timeProvider.GetUtcNow()),
            cancellationToken);
        await SendBoardingPassIfNeededAsync(booking, ticketResult, cancellationToken);

        return CharterBookingPassengerResultSupport.ToUpdateResult(
            booking,
            ticketResult?.Tickets,
            additionalInsuranceAmount);
    }

    private async Task SendBoardingPassIfNeededAsync(
        Booking booking,
        PassengerTicketEnsureResult? ticketResult,
        CancellationToken cancellationToken)
    {
        var ticket = ticketResult?.CreatedTickets.FirstOrDefault();
        if (ticket is null
            || string.IsNullOrWhiteSpace(booking.ContactEmail)
            || !string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase)
            || booking.RemainingAmount > 0)
        {
            return;
        }

        var paidPayment = booking.Payments
            .Where(x => PaymentSupport.IsPaid(x.PaymentStatus))
            .OrderByDescending(x => x.PaidAt ?? x.Created)
            .FirstOrDefault();
        if (paidPayment?.PaidAt is null)
        {
            return;
        }

        var bookingNotification = PaymentSupport.CreatePaymentSucceededNotification(booking, paidPayment);
        var attachments = CreateBoardingPassAttachments(booking, ticketResult!.Tickets);
        await _paymentNotificationSender.SendBoardingPassAsync(
            new BoardingPassNotification(
                bookingNotification,
                ticket.TicketCode,
                ticket.QrToken,
                Attachments: attachments,
                PassengerName: ticket.BookingPassenger?.FullName),
            cancellationToken);
    }

    private IReadOnlyList<EmailAttachment> CreateBoardingPassAttachments(
        Booking booking,
        IReadOnlyList<Ticket> tickets)
    {
        var export = CharterBookingTicketExportSupport.ToDto(booking, tickets);
        var pdfBytes = _ticketPdfRenderer.Render(export);

        return
        [
            new EmailAttachment(
                $"{SanitizeFileName(booking.BookingCode)}-boarding-pass.pdf",
                "application/pdf",
                pdfBytes)
        ];
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeValue = new string(value.Select(x => invalidChars.Contains(x) ? '-' : x).ToArray());
        return string.IsNullOrWhiteSpace(safeValue) ? "boarding-pass" : safeValue;
    }

    private static IReadOnlyList<CharterBookingPassengerRequest> NormalizePassengerRequests(
        Booking booking,
        IReadOnlyList<CharterBookingPassengerRequest>? passengers)
    {
        if (passengers is { Count: > 0 })
        {
            return passengers
                .Select(x => string.IsNullOrWhiteSpace(x.FullName)
                    && booking.PassengerCount.GetValueOrDefault() == 1
                    && !string.IsNullOrWhiteSpace(booking.ContactName)
                        ? x with { FullName = booking.ContactName }
                        : x)
                .ToList();
        }

        return booking.PassengerCount.GetValueOrDefault() == 1
            && !string.IsNullOrWhiteSpace(booking.ContactName)
                ? [new CharterBookingPassengerRequest(booking.ContactName, null)]
                : [];
    }

    private static string? ResolveInferredPassengerType(
        Booking booking,
        IReadOnlyCollection<CharterBookingPassengerRequest> passengers,
        CharterBookingPassengerRequest passenger)
    {
        if (!string.IsNullOrWhiteSpace(passenger.DateOfBirth)
            || passengers.Count != 1
            || booking.PassengerCount.GetValueOrDefault() != 1)
        {
            return null;
        }

        return (booking.AdultCount.GetValueOrDefault(), booking.ChildCount.GetValueOrDefault()) switch
        {
            (1, 0) => CharterBookingPassengerType.Adult.ToString(),
            (0, 1) => CharterBookingPassengerType.Child.ToString(),
            _ => null
        };
    }
}
