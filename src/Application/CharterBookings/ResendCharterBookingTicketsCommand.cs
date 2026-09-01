using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record ResendCharterBookingTicketsCommand(Guid BookingId)
    : IRequest<ResendCharterBookingTicketsResult>;

public sealed record ResendCharterBookingTicketsResult(
    string BookingCode,
    string? BookingQrToken,
    int TicketCount,
    int CreatedTicketCount,
    string? ContactEmail,
    IReadOnlyList<string> PassengerEmails);

public sealed class ResendCharterBookingTicketsCommandValidator
    : AbstractValidator<ResendCharterBookingTicketsCommand>
{
    public ResendCharterBookingTicketsCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
    }
}

public sealed class ResendCharterBookingTicketsCommandHandler
    : IRequestHandler<ResendCharterBookingTicketsCommand, ResendCharterBookingTicketsResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingTicketPdfRenderer? _ticketPdfRenderer;

    public ResendCharterBookingTicketsCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        ICharterBookingTicketPdfRenderer? ticketPdfRenderer = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentNotificationSender = paymentNotificationSender;
        _timeProvider = timeProvider;
        _ticketPdfRenderer = ticketPdfRenderer;
    }

    public async Task<ResendCharterBookingTicketsResult> Handle(
        ResendCharterBookingTicketsCommand request,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(
            _context,
            _userContext,
            cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.Boat)
            .Include(x => x.CharterBoats)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .Include(x => x.CharterRoute)
            .Include(x => x.Passengers)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        await CharterBookingAssignmentSupport.EnsureCanViewOperationalAsync(
            _context,
            currentUser,
            booking,
            includeCustomerOwner: true,
            notFoundWhenDenied: true,
            cancellationToken);

        if (booking.BookingStatus != BookingStatus.Confirmed
            || !string.Equals(booking.PaymentStatus, BookingPaymentStatusExtensions.PaidValue, StringComparison.OrdinalIgnoreCase)
            || booking.RemainingAmount > 0)
        {
            throw new ValidationException([new ValidationFailure("booking",
                "Chỉ gửi lại vé cho charter booking đã xác nhận và thanh toán đủ.")]);
        }

        var paidPayment = booking.Payments
            .Where(x => PaymentSupport.IsPaid(x.PaymentStatus) && x.PaidAt.HasValue)
            .OrderByDescending(x => x.PaidAt)
            .FirstOrDefault();

        if (paidPayment is null)
        {
            throw new ValidationException([new ValidationFailure("payment",
                "Charter booking chưa có thanh toán thành công nào để gửi vé.")]);
        }

        var preTicketCount = booking.Tickets.Count;
        var ticketResult = await CharterBookingTicketSupport.EnsurePassengerTicketsAsync(
            _context,
            booking,
            _timeProvider,
            cancellationToken);

        if (ticketResult is null || ticketResult.Tickets.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("tickets",
                "Charter booking chưa có hành khách đã duyệt để phát hành vé.")]);
        }

        if (ticketResult.CreatedTickets.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
            _context,
            _timeProvider,
            _paymentNotificationSender,
            booking,
            paidPayment,
            cancellationToken,
            _ticketPdfRenderer);

        var contactEmail = booking.ContactEmail?.Trim();
        var passengerEmails = booking.Passengers
            .Where(CharterBookingPassengerSupport.IsApproved)
            .Where(p => !string.IsNullOrWhiteSpace(p.Email))
            .Select(p => p.Email!.Trim())
            .Where(email => !string.Equals(email, contactEmail, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ResendCharterBookingTicketsResult(
            booking.BookingCode,
            booking.CharterBookingQrToken,
            ticketResult.Tickets.Count,
            ticketResult.CreatedTickets.Count,
            contactEmail,
            passengerEmails);
    }
}
