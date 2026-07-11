using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

/// <summary>
/// Gửi lại email vé điện tử (QR chung + QR riêng) cho booking thường đã thanh toán đủ.
/// Nếu vé/QR chung chưa được phát hành (vd webhook lỗi) thì phát hành bù trước khi gửi.
/// </summary>
public sealed record ResendBookingTicketsCommand(Guid BookingId) : IRequest<ResendBookingTicketsResult>;

public sealed record ResendBookingTicketsResult(
    string BookingCode,
    string? BookingQrToken,
    int TicketCount,
    string? BookerEmail,
    IReadOnlyList<string> PassengerEmails);

public sealed class ResendBookingTicketsCommandValidator : AbstractValidator<ResendBookingTicketsCommand>
{
    public ResendBookingTicketsCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
    }
}

public sealed class ResendBookingTicketsCommandHandler
    : IRequestHandler<ResendBookingTicketsCommand, ResendBookingTicketsResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly TimeProvider _timeProvider;
    private readonly IBookingTicketPdfRenderer? _bookingTicketPdfRenderer;

    public ResendBookingTicketsCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        IBookingTicketPdfRenderer? bookingTicketPdfRenderer = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentNotificationSender = paymentNotificationSender;
        _timeProvider = timeProvider;
        _bookingTicketPdfRenderer = bookingTicketPdfRenderer;
    }

    public async Task<ResendBookingTicketsResult> Handle(
        ResendBookingTicketsCommand request,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);

        var booking = await _context.Set<Booking>()
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(
                x => x.Id == request.BookingId && x.BookingType == Booking.SeatBookingType,
                cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        if (booking.UserId != currentUser.Id
            && !AuthSupport.IsAdmin(currentUser)
            && !AuthSupport.IsManager(currentUser)
            && !AuthSupport.IsStaff(currentUser))
        {
            throw new NotFoundException("Booking not found.");
        }

        if (booking.BookingStatus != BookingStatus.Confirmed
            || (!string.Equals(booking.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)
                && booking.RemainingAmount > 0))
        {
            throw new ValidationException([new ValidationFailure("booking",
                "Chỉ gửi lại vé cho booking đã xác nhận và thanh toán đủ.")]);
        }

        var paidPayment = booking.Payments
            .Where(x => PaymentSupport.IsPaid(x.PaymentStatus) && x.PaidAt.HasValue)
            .OrderByDescending(x => x.PaidAt)
            .FirstOrDefault()
            ?? throw new ValidationException([new ValidationFailure("payment",
                "Booking chưa có thanh toán thành công nào để gửi vé.")]);

        var tickets = await TicketIssueSupport.EnsureRegularBookingPassengerTicketsAsync(
            _context,
            booking,
            _timeProvider,
            cancellationToken);
        if (tickets.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("tickets",
                "Booking chưa có vé nào để gửi.")]);
        }

        await RegularBookingETicketSupport.SendETicketEmailsAsync(
            _context,
            _paymentNotificationSender,
            booking,
            paidPayment,
            tickets,
            cancellationToken,
            _bookingTicketPdfRenderer);

        var contactEmail = booking.ContactEmail?.Trim();
        var passengerEmails = await _context.Set<BookingPassenger>()
            .Where(p => p.BookingId == booking.Id && p.Email != null && p.Email != string.Empty)
            .Select(p => p.Email!)
            .ToListAsync(cancellationToken);

        return new ResendBookingTicketsResult(
            booking.BookingCode,
            booking.CharterBookingQrToken,
            tickets.Count,
            contactEmail,
            passengerEmails
                .Where(x => !string.Equals(x.Trim(), contactEmail, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
    }
}
