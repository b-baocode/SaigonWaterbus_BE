using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CharterBookingTicketReconciliationResult(
    int ReconciledBookingCount,
    int IssuedTicketCount);

public interface ICharterBookingTicketReconciliationProcessor
{
    Task<CharterBookingTicketReconciliationResult> ReconcileAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Recovers tickets missed by an interrupted or legacy payment callback.
/// A booking is processed only while at least one approved passenger has no active ticket.
/// </summary>
public sealed class CharterBookingTicketReconciliationProcessor
    : ICharterBookingTicketReconciliationProcessor
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IPaymentNotificationSender _paymentNotificationSender;

    public CharterBookingTicketReconciliationProcessor(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        IPaymentNotificationSender paymentNotificationSender)
    {
        _context = context;
        _timeProvider = timeProvider;
        _paymentNotificationSender = paymentNotificationSender;
    }

    public async Task<CharterBookingTicketReconciliationResult> ReconcileAsync(
        CancellationToken cancellationToken)
    {
        var bookings = await _context.Set<Booking>()
            .Where(x => x.BookingType == Booking.CharterBookingType
                && x.BookingStatus == BookingStatus.Confirmed
                && x.RemainingAmount <= 0
                && x.PaymentStatus == BookingPaymentStatusExtensions.PaidValue)
            .Include(x => x.Passengers)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .Include(x => x.Payments)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.CharterRoute)
            .ToListAsync(cancellationToken);

        var reconciledBookingCount = 0;
        var issuedTicketCount = 0;

        foreach (var booking in bookings)
        {
            var approvedPassengerIds = booking.Passengers
                .Where(CharterBookingPassengerSupport.IsApproved)
                .Select(x => x.Id)
                .ToHashSet();
            if (approvedPassengerIds.Count == 0)
            {
                continue;
            }

            var ticketedPassengerIds = booking.Tickets
                .Where(x => x.BookingPassengerId.HasValue
                    && x.TicketStatus is not TicketStatus.Cancelled and not TicketStatus.Expired)
                .Select(x => x.BookingPassengerId!.Value)
                .ToHashSet();
            if (approvedPassengerIds.All(ticketedPassengerIds.Contains))
            {
                continue;
            }

            var latestPaidPayment = booking.Payments
                .Where(x => PaymentSupport.IsPaid(x.PaymentStatus) && x.PaidAt.HasValue)
                .OrderByDescending(x => x.PaidAt)
                .FirstOrDefault();
            if (latestPaidPayment is null)
            {
                continue;
            }

            var ticketResult = await CharterBookingTicketSupport.EnsurePassengerTicketsAsync(
                _context,
                booking,
                _timeProvider,
                cancellationToken);
            if (ticketResult is null || ticketResult.CreatedTickets.Count == 0)
            {
                continue;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
                _context,
                _timeProvider,
                _paymentNotificationSender,
                booking,
                latestPaidPayment,
                cancellationToken);

            reconciledBookingCount++;
            issuedTicketCount += ticketResult.CreatedTickets.Count;
        }

        return new CharterBookingTicketReconciliationResult(
            reconciledBookingCount,
            issuedTicketCount);
    }
}
