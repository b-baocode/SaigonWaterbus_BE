using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CustomBookings;

public sealed record UpdateCustomBookingPassengersCommand(
    Guid BookingId,
    IReadOnlyList<CustomBookingPassengerRequest> Passengers)
    : IRequest<UpdateCustomBookingPassengersResult>;

public sealed class UpdateCustomBookingPassengersCommandValidator
    : AbstractValidator<UpdateCustomBookingPassengersCommand>
{
    public UpdateCustomBookingPassengersCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.Passengers).NotNull();
        RuleForEach(x => x.Passengers).SetValidator(new CustomBookingPassengerRequestValidator());
    }
}

public sealed class UpdateCustomBookingPassengersCommandHandler
    : IRequestHandler<UpdateCustomBookingPassengersCommand, UpdateCustomBookingPassengersResult>
{
    private const string PaidBookingPaymentStatus = "Paid";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly ICustomBookingTicketPdfRenderer _ticketPdfRenderer;
    private readonly TimeProvider _timeProvider;

    public UpdateCustomBookingPassengersCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IPaymentNotificationSender paymentNotificationSender,
        ICustomBookingTicketPdfRenderer ticketPdfRenderer,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _paymentNotificationSender = paymentNotificationSender;
        _ticketPdfRenderer = ticketPdfRenderer;
        _timeProvider = timeProvider;
    }

    public async Task<UpdateCustomBookingPassengersResult> Handle(
        UpdateCustomBookingPassengersCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var booking = await CustomBookingQuerySupport.BuildBaseQuery(_context)
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
            ?? throw new NotFoundException("Custom booking not found.");

        if (booking.UserId != userId)
        {
            throw new NotFoundException("Custom booking not found.");
        }

        if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.Refunded)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể cập nhật danh sách hành khách cho booking đã hủy hoặc đã hoàn tất.")]);
        }

        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PaymentStatus),
                "Chỉ nhập danh sách hành khách sau khi custom booking đã thanh toán đủ.")]);
        }

        if (request.Passengers.Count > booking.PassengerCount.GetValueOrDefault())
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Passengers),
                "Danh sách hành khách không được vượt quá số khách đã đăng ký.")]);
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        var passengers = request.Passengers
            .Select(x => CustomBookingPassengerSupport.ToEntity(booking.Id, x, today))
            .ToList();
        CustomBookingPassengerSupport.EnsurePassengerTypeCountsMatchRequest(
            booking,
            passengers,
            nameof(request.Passengers));
        CustomBookingTicketSupport.CancelTicketsBeforeReplacingPassengers(booking);
        _context.Set<BookingPassenger>().RemoveRange(booking.Passengers);
        booking.Passengers = passengers;
        var ticketResult = await CustomBookingTicketSupport.EnsurePassengerTicketsAsync(
            _context,
            booking,
            _timeProvider,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await SendBoardingPassIfNeededAsync(booking, ticketResult, cancellationToken);

        var adultCount = CustomBookingPassengerSupport.CountAdults(booking.Passengers);
        var childCount = CustomBookingPassengerSupport.CountChildren(booking.Passengers);
        var ticketDtos = ticketResult?.Tickets
            .Select(CustomBookingTicketSupport.ToDto)
            .ToList() ?? [];

        return new UpdateCustomBookingPassengersResult(
            booking.Id,
            booking.CustomBookingQrToken,
            booking.PassengerCount.GetValueOrDefault(),
            booking.Passengers.Count,
            adultCount,
            childCount,
            booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(CustomBookingPassengerSupport.ToDto)
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
        var export = CustomBookingTicketExportSupport.ToDto(booking, tickets);
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
