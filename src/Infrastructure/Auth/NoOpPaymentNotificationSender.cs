using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class NoOpPaymentNotificationSender : IPaymentNotificationSender
{
    private readonly ILogger<NoOpPaymentNotificationSender> _logger;

    public NoOpPaymentNotificationSender(ILogger<NoOpPaymentNotificationSender> logger)
    {
        _logger = logger;
    }

    public Task SendPaymentSucceededAsync(
        PaymentSucceededNotification notification,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Payment notification skipped. BookingCode: {BookingCode}, PaymentCode: {PaymentCode}, Email: {Email}, FullyPaid: {IsFullyPaid}",
            notification.BookingCode,
            notification.PaymentCode,
            notification.Email,
            notification.IsFullyPaid);

        return Task.CompletedTask;
    }

    public Task SendBoardingPassAsync(
        BoardingPassNotification notification,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Boarding pass notification skipped. BookingCode: {BookingCode}, TicketCode: {TicketCode}, Email: {Email}",
            notification.Booking.BookingCode,
            notification.TicketCode,
            notification.Booking.Email);

        return Task.CompletedTask;
    }
}
