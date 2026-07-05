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

    public UpdateCharterBookingPassengersCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IPaymentNotificationSender paymentNotificationSender,
        ICharterBookingTicketPdfRenderer ticketPdfRenderer,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _paymentNotificationSender = paymentNotificationSender;
        _ticketPdfRenderer = ticketPdfRenderer;
        _timeProvider = timeProvider;
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

        if (request.Passengers.Count > booking.PassengerCount.GetValueOrDefault())
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Passengers),
                "Danh sách hành khách không được vượt quá số khách đã đăng ký.")]);
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        var passengers = request.Passengers
            .Select(x => CharterBookingPassengerSupport.ToEntity(booking.Id, x, today))
            .ToList();
        CharterBookingPassengerSupport.EnsurePassengerTypeCountsMatchRequest(
            booking,
            passengers,
            nameof(request.Passengers));
        CharterBookingTicketSupport.CancelTicketsBeforeReplacingPassengers(booking);
        _context.Set<BookingPassenger>().RemoveRange(booking.Passengers);
        booking.Passengers = passengers;
        var ticketResult = await CharterBookingTicketSupport.EnsurePassengerTicketsAsync(
            _context,
            booking,
            _timeProvider,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await SendBoardingPassIfNeededAsync(booking, ticketResult, cancellationToken);

        var adultCount = CharterBookingPassengerSupport.CountAdults(booking.Passengers);
        var childCount = CharterBookingPassengerSupport.CountChildren(booking.Passengers);
        var ticketDtos = ticketResult?.Tickets
            .Select(CharterBookingTicketSupport.ToDto)
            .ToList() ?? [];

        return new UpdateCharterBookingPassengersResult(
            booking.Id,
            booking.CharterBookingQrToken,
            booking.PassengerCount.GetValueOrDefault(),
            booking.Passengers.Count,
            adultCount,
            childCount,
            booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(CharterBookingPassengerSupport.ToDto)
                .ToList(),
            ticketDtos.Count,
            ticketDtos);
    }

    private async Task SendBoardingPassIfNeededAsync(
        Booking booking,
        PassengerTicketEnsureResult? ticketResult,
        CancellationToken cancellationToken)
    {
        var ticket = ticketResult?.CreatedTickets.FirstOrDefault();
        if (ticket is null || string.IsNullOrWhiteSpace(booking.ContactEmail))
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
                Attachments: attachments),
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
}
