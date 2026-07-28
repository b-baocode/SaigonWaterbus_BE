using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record ApproveCharterBookingPassengerAddRequestCommand(
    Guid BookingId,
    Guid RequestBatchId,
    string? Note = null)
    : IRequest<UpdateCharterBookingPassengersResult>;

public sealed record RejectCharterBookingPassengerAddRequestCommand(
    Guid BookingId,
    Guid RequestBatchId,
    string Note)
    : IRequest<UpdateCharterBookingPassengersResult>;

public sealed class ApproveCharterBookingPassengerAddRequestCommandValidator
    : AbstractValidator<ApproveCharterBookingPassengerAddRequestCommand>
{
    public ApproveCharterBookingPassengerAddRequestCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.RequestBatchId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
    }
}

public sealed class RejectCharterBookingPassengerAddRequestCommandValidator
    : AbstractValidator<RejectCharterBookingPassengerAddRequestCommand>
{
    public RejectCharterBookingPassengerAddRequestCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.RequestBatchId).NotEmpty();
        RuleFor(x => x.Note)
            .NotEmpty()
            .MaximumLength(500)
            .WithMessage("Lý do từ chối là bắt buộc.");
    }
}

public sealed class ApproveCharterBookingPassengerAddRequestCommandHandler
    : IRequestHandler<ApproveCharterBookingPassengerAddRequestCommand, UpdateCharterBookingPassengersResult>
{
    private const string PaidBookingPaymentStatus = "Paid";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly ICharterBookingTicketPdfRenderer _ticketPdfRenderer;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public ApproveCharterBookingPassengerAddRequestCommandHandler(
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
        ApproveCharterBookingPassengerAddRequestCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var booking = await LoadBookingAsync(request.BookingId, cancellationToken);
        EnsureCanReview(actor, booking);
        EnsureBookingCanApprove(booking, request.RequestBatchId);

        var now = _timeProvider.GetUtcNow();
        CharterBookingPassengerSupport.EnsureManifestCanBeUpdatedBeforeCutoff(
            booking,
            now,
            nameof(request.RequestBatchId));

        var pendingPassengers = GetPendingBatch(booking, request.RequestBatchId);
        var approvedCount = booking.Passengers.Count(CharterBookingPassengerSupport.IsApproved);
        CharterBookingPassengerSupport.EnsurePassengerCountDoesNotExceedSelectedBoatCapacity(
            booking,
            approvedCount + pendingPassengers.Count,
            nameof(request.RequestBatchId));

        foreach (var passenger in pendingPassengers)
        {
            passenger.ApprovalStatus = CharterBookingPassengerSupport.ApprovalStatusApproved;
            passenger.ReviewedAt = now;
            passenger.ReviewedByUserId = actor.Id;
            passenger.ReviewNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        }

        var approvedPassengers = booking.Passengers
            .Where(CharterBookingPassengerSupport.IsApproved)
            .ToList();
        booking.PassengerCount = approvedPassengers.Count;
        booking.AdultCount = CharterBookingPassengerSupport.CountAdults(approvedPassengers);
        booking.ChildCount = CharterBookingPassengerSupport.CountChildren(approvedPassengers);

        var ticketResult = await CharterBookingTicketSupport.EnsurePassengerTicketsAsync(
            _context,
            booking,
            _timeProvider,
            cancellationToken);
        var additionalInsuranceAmount = CharterBookingInsuranceSupport.ApplyPassengerQuantityIncrease(
            booking,
            approvedPassengers.Count,
            now);

        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "PassengerAddApproved",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                now),
            cancellationToken);
        await SendBoardingPassIfNeededAsync(booking, ticketResult, cancellationToken);

        return ToResult(booking, ticketResult?.Tickets, additionalInsuranceAmount);
    }

    private async Task<Booking> LoadBookingAsync(Guid bookingId, CancellationToken cancellationToken) =>
        await CharterBookingQuerySupport.BuildBaseQuery(_context)
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
            .SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

    private static void EnsureCanReview(User actor, Booking booking)
    {
        if (AuthSupport.IsAdmin(actor)
            || (AuthSupport.IsManager(actor) && booking.AssignedManagerId == actor.Id))
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    private static void EnsureBookingCanApprove(Booking booking, Guid requestBatchId)
    {
        if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.Refunded)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể duyệt thêm hành khách cho booking đã hủy hoặc đã hoàn tất.")]);
        }

        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PaymentStatus),
                "Chỉ duyệt và phát hành vé bổ sung sau khi charter booking đã thanh toán đủ.")]);
        }

        if (booking.Tickets.Any(x => x.TicketStatus is TicketStatus.CheckedIn or TicketStatus.CheckedOut))
        {
            throw new ValidationException([new ValidationFailure(nameof(requestBatchId),
                "Không thể duyệt thêm hành khách khi đã có vé check-in hoặc check-out.")]);
        }
    }

    private static IReadOnlyList<BookingPassenger> GetPendingBatch(Booking booking, Guid requestBatchId)
    {
        var pendingPassengers = booking.Passengers
            .Where(x => x.RequestBatchId == requestBatchId
                && CharterBookingPassengerSupport.IsPending(x))
            .ToList();
        if (pendingPassengers.Count == 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(requestBatchId),
                "Không tìm thấy yêu cầu thêm hành khách đang chờ duyệt.")]);
        }

        return pendingPassengers;
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

    private static UpdateCharterBookingPassengersResult ToResult(
        Booking booking,
        IReadOnlyList<Ticket>? tickets = null,
        decimal additionalInsuranceAmount = 0m) =>
        CharterBookingPassengerResultSupport.ToUpdateResult(booking, tickets, additionalInsuranceAmount);

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeValue = new string(value.Select(x => invalidChars.Contains(x) ? '-' : x).ToArray());
        return string.IsNullOrWhiteSpace(safeValue) ? "boarding-pass" : safeValue;
    }
}

public sealed class RejectCharterBookingPassengerAddRequestCommandHandler
    : IRequestHandler<RejectCharterBookingPassengerAddRequestCommand, UpdateCharterBookingPassengersResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public RejectCharterBookingPassengerAddRequestCommandHandler(
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

    public async Task<UpdateCharterBookingPassengersResult> Handle(
        RejectCharterBookingPassengerAddRequestCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Note),
                "Lý do từ chối là bắt buộc.")]);
        }

        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.Passengers)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (!AuthSupport.IsAdmin(actor)
            && !(AuthSupport.IsManager(actor) && booking.AssignedManagerId == actor.Id))
        {
            throw new ForbiddenAccessException();
        }

        var pendingPassengers = booking.Passengers
            .Where(x => x.RequestBatchId == request.RequestBatchId
                && CharterBookingPassengerSupport.IsPending(x))
            .ToList();
        if (pendingPassengers.Count == 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.RequestBatchId),
                "Không tìm thấy yêu cầu thêm hành khách đang chờ duyệt.")]);
        }

        var now = _timeProvider.GetUtcNow();
        foreach (var passenger in pendingPassengers)
        {
            passenger.ApprovalStatus = CharterBookingPassengerSupport.ApprovalStatusRejected;
            passenger.ReviewedAt = now;
            passenger.ReviewedByUserId = actor.Id;
            passenger.ReviewNote = request.Note.Trim();
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "PassengerAddRejected",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                now),
            cancellationToken);

        return CharterBookingPassengerResultSupport.ToUpdateResult(booking);
    }
}
