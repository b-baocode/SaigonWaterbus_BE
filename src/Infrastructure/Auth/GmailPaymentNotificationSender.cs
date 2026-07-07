using System.Globalization;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class GmailPaymentNotificationSender : IPaymentNotificationSender
{
    private readonly IOptionsMonitor<GmailOptions> _optionsMonitor;
    private readonly ILogger<GmailPaymentNotificationSender> _logger;

    public GmailPaymentNotificationSender(
        IOptionsMonitor<GmailOptions> optionsMonitor,
        ILogger<GmailPaymentNotificationSender> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task SendPaymentSucceededAsync(
        PaymentSucceededNotification notification,
        CancellationToken cancellationToken)
    {
        await SendAsync(
            notification.Email,
            BuildSubject(notification),
            BuildBody(notification),
            notification.BookingCode,
            notification.PaymentCode,
            cancellationToken);
    }

    public async Task SendBoardingPassAsync(
        BoardingPassNotification notification,
        CancellationToken cancellationToken)
    {
        var body = BuildBody(notification.Booking)
            + "\n\n"
            + $"Ma ve: {notification.TicketCode}\n"
            + $"QR token: {notification.QrToken}\n"
            + (string.IsNullOrWhiteSpace(notification.QrImageUrl)
                ? string.Empty
                : $"QR image: {notification.QrImageUrl}\n");

        await SendAsync(
            notification.Booking.Email,
            $"Saigon Waterbus - Ve len tau {notification.Booking.BookingCode}",
            body,
            notification.Booking.BookingCode,
            notification.TicketCode,
            cancellationToken);
    }

    public async Task SendETicketsAsync(
        ETicketNotification notification,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var body = new System.Text.StringBuilder();
        body.AppendLine($"Xin chao {notification.Booking.ContactName},");
        body.AppendLine();
        body.AppendLine($"Saigon Waterbus xac nhan thanh toan thanh cong cho booking {notification.Booking.BookingCode}.");
        if (!string.IsNullOrWhiteSpace(notification.TripCode))
        {
            body.AppendLine($"Chuyen: {notification.TripCode}");
        }

        if (!string.IsNullOrWhiteSpace(notification.FromStationName) || !string.IsNullOrWhiteSpace(notification.ToStationName))
        {
            body.AppendLine($"Hanh trinh: {notification.FromStationName} -> {notification.ToStationName}");
        }

        if (notification.DepartureTime.HasValue)
        {
            body.AppendLine($"Khoi hanh: {notification.DepartureTime.Value:dd/MM/yyyy HH:mm zzz}");
        }

        body.AppendLine();
        if (!string.IsNullOrWhiteSpace(notification.BookingQrToken))
        {
            body.AppendLine($"QR chung cua booking (check-in ca nhom): {notification.BookingQrToken}");
            AppendQrImageUrl(body, options, notification.BookingQrToken);
            body.AppendLine();
        }

        body.AppendLine("Ve cua tung hanh khach:");
        foreach (var ticket in notification.Tickets)
        {
            body.AppendLine($"- {ticket.PassengerName}"
                + (string.IsNullOrWhiteSpace(ticket.SeatCode) ? string.Empty : $" | Ghe {ticket.SeatCode}")
                + (string.IsNullOrWhiteSpace(ticket.TicketTypeName) ? string.Empty : $" | {ticket.TicketTypeName}"));
            body.AppendLine($"  Ma ve: {ticket.TicketCode}");
            body.AppendLine($"  QR: {ticket.QrToken}");
            AppendQrImageUrl(body, options, ticket.QrToken, indent: "  ");
        }

        body.AppendLine();
        body.AppendLine("Vui long xuat trinh ma QR khi len tau. Cam on ban da su dung dich vu Saigon Waterbus.");

        await SendAsync(
            notification.Booking.Email,
            $"Saigon Waterbus - Ve dien tu {notification.Booking.BookingCode}",
            body.ToString(),
            notification.Booking.BookingCode,
            notification.Booking.PaymentCode,
            cancellationToken);
    }

    private static void AppendQrImageUrl(
        System.Text.StringBuilder body,
        GmailOptions options,
        string qrPayload,
        string indent = "")
    {
        if (string.IsNullOrWhiteSpace(options.PublicApiBaseUrl))
        {
            return;
        }

        body.AppendLine(
            $"{indent}Anh QR: {options.PublicApiBaseUrl.TrimEnd('/')}/api/tickets/qr-image/{Uri.EscapeDataString(qrPayload)}");
    }

    private async Task SendAsync(
        string email,
        string subject,
        string body,
        string bookingCode,
        string referenceCode,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
        {
            _logger.LogWarning("Gmail payment notification is enabled but Username/Password is missing.");
            return;
        }

        var fromEmail = string.IsNullOrWhiteSpace(options.FromEmail)
            ? options.Username
            : options.FromEmail;

        using var message = new MailMessage
        {
            From = new MailAddress(fromEmail, options.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(email);

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(options.Username, options.Password),
            Timeout = 10000
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(message);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Gmail payment notification sent. BookingCode: {BookingCode}, PaymentCode: {PaymentCode}, Email: {Email}",
                bookingCode,
                referenceCode,
                email);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(
                "Gmail payment notification failed: {Message}. BookingCode: {BookingCode}, PaymentCode: {PaymentCode}, Email: {Email}",
                ex.Message,
                bookingCode,
                referenceCode,
                email);
        }
    }

    private static string BuildSubject(PaymentSucceededNotification notification) =>
        notification.IsFullyPaid
            ? $"Saigon Waterbus - Xac nhan thanh toan du {notification.BookingCode}"
            : $"Saigon Waterbus - Xac nhan dat coc {notification.BookingCode}";

    private static string BuildBody(PaymentSucceededNotification notification)
    {
        var paidLabel = notification.IsFullyPaid ? "thanh toan du" : "dat coc";

        return
            $"Xin chao {notification.ContactName},\n\n"
            + $"Saigon Waterbus da ghi nhan {paidLabel} cho booking {notification.BookingCode}.\n"
            + $"Ma thanh toan: {notification.PaymentCode}\n"
            + $"Loai thanh toan: {notification.PaymentPurpose}\n"
            + $"So tien da thanh toan: {FormatMoney(notification.PaymentAmount, notification.Currency)}\n"
            + $"Tong gia tri booking: {FormatMoney(notification.BookingTotalAmount, notification.Currency)}\n"
            + $"Da dat coc/da thanh toan: {FormatMoney(notification.DepositAmount, notification.Currency)}\n"
            + $"Con lai: {FormatMoney(notification.RemainingAmount, notification.Currency)}\n"
            + $"Trang thai thanh toan: {notification.BookingPaymentStatus}\n"
            + $"Thoi gian ghi nhan: {notification.PaidAt:O}\n\n"
            + "Cam on ban da su dung dich vu Saigon Waterbus.";
    }

    private static string FormatMoney(decimal amount, string currency) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{amount:N0} {currency}");
}
