using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QRCoder;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class GmailPaymentNotificationSender : IPaymentNotificationSender
{
    // Palette đồng bộ với boarding pass PDF (QuestPdf*TicketPdfRenderer).
    private const string Navy = "#073B56";
    private const string Teal = "#057C9F";
    private const string Gold = "#F1C85D";
    private const string Ink = "#283640";
    private const string Muted = "#6B7780";
    private const string Pale = "#F3FAFC";
    private const string Line = "#D9E7EE";

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

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
            htmlBody: null,
            attachments: null,
            inlineImages: null,
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

        var inlineImages = new List<InlineImage>
        {
            new("qr-boarding", BuildQrPngBytes(notification.QrToken))
        };

        await SendAsync(
            notification.Booking.Email,
            $"Saigon Waterbus - Ve len tau {notification.Booking.BookingCode}",
            body,
            BuildBoardingPassHtml(notification),
            notification.Attachments,
            inlineImages,
            notification.Booking.BookingCode,
            notification.TicketCode,
            cancellationToken);
    }

    public async Task SendETicketsAsync(
        ETicketNotification notification,
        CancellationToken cancellationToken)
    {
        var inlineImages = new List<InlineImage>();
        string? groupQrCid = null;
        if (!string.IsNullOrWhiteSpace(notification.BookingQrToken))
        {
            groupQrCid = "qr-group";
            inlineImages.Add(new InlineImage(groupQrCid, BuildQrPngBytes(notification.BookingQrToken)));
        }

        var ticketCids = new List<(ETicketPassenger Ticket, string Cid)>();
        for (var i = 0; i < notification.Tickets.Count; i++)
        {
            var cid = $"qr-ticket-{i}";
            ticketCids.Add((notification.Tickets[i], cid));
            inlineImages.Add(new InlineImage(cid, BuildQrPngBytes(notification.Tickets[i].QrToken)));
        }

        await SendAsync(
            notification.Booking.Email,
            $"Saigon Waterbus - Ve dien tu {notification.Booking.BookingCode}",
            BuildETicketPlainText(notification),
            BuildETicketHtml(notification, groupQrCid, ticketCids),
            notification.Attachments,
            inlineImages,
            notification.Booking.BookingCode,
            notification.Booking.PaymentCode,
            cancellationToken);
    }

    private static string BuildETicketPlainText(ETicketNotification notification)
    {
        var options = new StringBuilder();
        options.AppendLine($"Xin chao {notification.Booking.ContactName},");
        options.AppendLine();
        options.AppendLine($"Saigon Waterbus xac nhan thanh toan thanh cong cho booking {notification.Booking.BookingCode}.");
        if (!string.IsNullOrWhiteSpace(notification.TripCode))
        {
            options.AppendLine($"Chuyen: {notification.TripCode}");
        }

        if (!string.IsNullOrWhiteSpace(notification.FromStationName) || !string.IsNullOrWhiteSpace(notification.ToStationName))
        {
            options.AppendLine($"Hanh trinh: {notification.FromStationName} -> {notification.ToStationName}");
        }

        if (notification.DepartureTime.HasValue)
        {
            options.AppendLine($"Khoi hanh: {FormatVietnamTime(notification.DepartureTime.Value)} (gio Viet Nam)");
        }

        options.AppendLine();
        if (!string.IsNullOrWhiteSpace(notification.BookingQrToken))
        {
            options.AppendLine($"QR chung cua booking (check-in ca nhom): {notification.BookingQrToken}");
        }

        options.AppendLine("Ve cua tung hanh khach:");
        foreach (var ticket in notification.Tickets)
        {
            options.AppendLine($"- {ticket.PassengerName}"
                + (string.IsNullOrWhiteSpace(ticket.SeatCode) ? string.Empty : $" | Ghe {ticket.SeatCode}")
                + (string.IsNullOrWhiteSpace(ticket.TicketTypeName) ? string.Empty : $" | {ticket.TicketTypeName}"));
            options.AppendLine($"  Ma ve: {ticket.TicketCode}");
        }

        options.AppendLine();
        options.AppendLine("Vui long xuat trinh ma QR khi len tau. Cam on ban da su dung dich vu Saigon Waterbus.");
        return options.ToString();
    }

    private static string BuildETicketHtml(
        ETicketNotification notification,
        string? groupQrCid,
        IReadOnlyList<(ETicketPassenger Ticket, string Cid)> ticketCids)
    {
        var booking = notification.Booking;
        var html = new StringBuilder();

        html.Append($"""
            <div style="margin:0;padding:24px 8px;background:#EEF6F8;font-family:'Segoe UI',Arial,sans-serif;">
              <table role="presentation" align="center" width="640" cellpadding="0" cellspacing="0" style="max-width:640px;width:100%;margin:0 auto;border-collapse:collapse;">
                <tr><td style="background:{Gold};height:8px;font-size:0;">&nbsp;</td></tr>
                <tr>
                  <td style="background:{Navy};padding:20px 24px;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>
                      <td>
                        <div style="font-size:22px;font-weight:bold;color:#FFFFFF;letter-spacing:1px;">WATERBUS</div>
                        <div style="font-size:11px;font-weight:600;color:{Gold};padding-top:4px;">E-TICKET / VE DIEN TU</div>
                      </td>
                      <td align="right">
                        <div style="font-size:10px;font-weight:600;color:#B8D7E4;">BOOKING CODE</div>
                        <div style="font-size:16px;font-weight:bold;color:#FFFFFF;">{WebUtility.HtmlEncode(booking.BookingCode)}</div>
                      </td>
                    </tr></table>
                  </td>
                </tr>
                <tr>
                  <td style="background:#FFFFFF;padding:24px;">
                    <p style="margin:0 0 16px 0;font-size:14px;color:{Ink};">Xin chao <b>{WebUtility.HtmlEncode(booking.ContactName)}</b>,<br/>
                    Saigon Waterbus xac nhan thanh toan thanh cong. Duoi day la ve dien tu cua ban.</p>
            """);

        // Khối hành trình
        html.Append($"""
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Pale};border:1px solid {Line};border-collapse:collapse;margin-bottom:16px;">
                      <tr>
                        <td style="padding:16px;">
                          <div style="font-size:10px;font-weight:600;color:{Muted};">FROM</div>
                          <div style="font-size:18px;font-weight:bold;color:{Teal};">{WebUtility.HtmlEncode(notification.FromStationName ?? "Diem di")}</div>
                        </td>
                        <td align="center" style="padding:16px;font-size:20px;font-weight:bold;color:{Gold};">&rarr;</td>
                        <td align="right" style="padding:16px;">
                          <div style="font-size:10px;font-weight:600;color:{Muted};">TO</div>
                          <div style="font-size:18px;font-weight:bold;color:{Teal};">{WebUtility.HtmlEncode(notification.ToStationName ?? "Diem den")}</div>
                        </td>
                      </tr>
                    </table>
            """);

        // Thông tin chuyến
        html.Append($"""
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;margin-bottom:16px;">
                      <tr>
                        {BuildInfoCellHtml("Chuyen", notification.TripCode)}
                        {BuildInfoCellHtml("Khoi hanh (GMT+7)", notification.DepartureTime.HasValue ? FormatVietnamTime(notification.DepartureTime.Value) : null)}
                        {BuildInfoCellHtml("Tuyen", notification.RouteName)}
                      </tr>
                    </table>
            """);

        // QR tổng — chỉ có trong email người đặt vé
        if (groupQrCid is not null)
        {
            html.Append($"""
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Pale};border:1px dashed {Teal};border-collapse:collapse;margin-bottom:20px;">
                      <tr>
                        <td style="padding:16px;">
                          <div style="font-size:12px;font-weight:bold;color:{Navy};">QR TONG - CHECK-IN CA NHOM</div>
                          <div style="font-size:11px;color:{Muted};padding-top:6px;">Ma nay chi danh cho nguoi dat ve.<br/>Staff quet 1 lan de check-in toan bo {notification.Tickets.Count} hanh khach.</div>
                        </td>
                        <td align="right" style="padding:16px;">
                          <img src="cid:{groupQrCid}" width="140" height="140" alt="QR tong" style="display:block;border:1px solid {Line};background:#FFFFFF;padding:6px;"/>
                        </td>
                      </tr>
                    </table>
            """);
        }

        // Vé từng hành khách
        foreach (var (ticket, cid) in ticketCids)
        {
            var seat = string.IsNullOrWhiteSpace(ticket.SeatCode) ? "Tu do" : ticket.SeatCode;
            var type = string.IsNullOrWhiteSpace(ticket.TicketTypeName) ? "-" : ticket.TicketTypeName;
            html.Append($"""
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid {Line};border-collapse:collapse;margin-bottom:12px;">
                      <tr>
                        <td style="padding:14px 16px;border-left:4px solid {Gold};">
                          <div style="font-size:10px;font-weight:600;color:{Muted};">PASSENGER / HANH KHACH</div>
                          <div style="font-size:17px;font-weight:bold;color:{Ink};padding:2px 0 8px 0;">{WebUtility.HtmlEncode(ticket.PassengerName)}</div>
                          <table role="presentation" cellpadding="0" cellspacing="0" style="border-collapse:collapse;"><tr>
                            <td style="padding-right:18px;">
                              <div style="font-size:9px;font-weight:600;color:#7C8B95;">SEAT</div>
                              <div style="font-size:13px;font-weight:bold;color:{Teal};">{WebUtility.HtmlEncode(seat)}</div>
                            </td>
                            <td style="padding-right:18px;">
                              <div style="font-size:9px;font-weight:600;color:#7C8B95;">TICKET TYPE</div>
                              <div style="font-size:13px;font-weight:bold;color:{Ink};">{WebUtility.HtmlEncode(type)}</div>
                            </td>
                            <td>
                              <div style="font-size:9px;font-weight:600;color:#7C8B95;">TICKET CODE</div>
                              <div style="font-size:13px;font-weight:bold;color:{Ink};">{WebUtility.HtmlEncode(ticket.TicketCode)}</div>
                            </td>
                          </tr></table>
                        </td>
                        <td align="right" width="150" style="padding:14px 16px;">
                          <img src="cid:{cid}" width="120" height="120" alt="QR ve {WebUtility.HtmlEncode(ticket.TicketCode)}" style="display:block;border:1px solid {Line};background:#FFFFFF;padding:4px;"/>
                          <div style="font-size:9px;color:{Muted};text-align:center;padding-top:4px;">Quet QR de check-in</div>
                        </td>
                      </tr>
                    </table>
            """);
        }

        html.Append($"""
                    <p style="margin:16px 0 0 0;padding-top:12px;border-top:1px solid {Line};font-size:11px;color:{Muted};">
                      Vui long xuat trinh ma QR (tren email nay hoac trong file PDF dinh kem) khi len tau.<br/>
                      Cam on ban da su dung dich vu Saigon Waterbus.
                    </p>
                  </td>
                </tr>
                <tr><td style="background:{Navy};padding:10px 24px;font-size:10px;color:#B8D7E4;" align="center">Saigon Waterbus &bull; waterbus</td></tr>
              </table>
            </div>
            """);

        return html.ToString();
    }

    private string BuildBoardingPassHtml(BoardingPassNotification notification)
    {
        var booking = notification.Booking;
        return $"""
            <div style="margin:0;padding:24px 8px;background:#EEF6F8;font-family:'Segoe UI',Arial,sans-serif;">
              <table role="presentation" align="center" width="640" cellpadding="0" cellspacing="0" style="max-width:640px;width:100%;margin:0 auto;border-collapse:collapse;">
                <tr><td style="background:{Gold};height:8px;font-size:0;">&nbsp;</td></tr>
                <tr>
                  <td style="background:{Navy};padding:20px 24px;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>
                      <td>
                        <div style="font-size:22px;font-weight:bold;color:#FFFFFF;letter-spacing:1px;">WATERBUS</div>
                        <div style="font-size:11px;font-weight:600;color:{Gold};padding-top:4px;">BOARDING PASS / VE LEN TAU</div>
                      </td>
                      <td align="right">
                        <div style="font-size:10px;font-weight:600;color:#B8D7E4;">TICKET CODE</div>
                        <div style="font-size:16px;font-weight:bold;color:#FFFFFF;">{WebUtility.HtmlEncode(notification.TicketCode)}</div>
                        <div style="font-size:10px;color:#B8D7E4;padding-top:4px;">Booking {WebUtility.HtmlEncode(booking.BookingCode)}</div>
                      </td>
                    </tr></table>
                  </td>
                </tr>
                <tr>
                  <td style="background:#FFFFFF;padding:24px;">
                    <p style="margin:0 0 16px 0;font-size:14px;color:{Ink};">Xin chao <b>{WebUtility.HtmlEncode(booking.ContactName)}</b>,<br/>
                    Saigon Waterbus da ghi nhan thanh toan cho booking <b>{WebUtility.HtmlEncode(booking.BookingCode)}</b>.</p>
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Pale};border:1px solid {Line};border-collapse:collapse;">
                      <tr>
                        <td style="padding:16px;">
                          <div style="font-size:10px;font-weight:600;color:{Muted};">SO TIEN DA THANH TOAN</div>
                          <div style="font-size:18px;font-weight:bold;color:{Teal};">{FormatMoney(notification.Booking.PaymentAmount, notification.Booking.Currency)}</div>
                          <div style="font-size:11px;color:{Muted};padding-top:6px;">Trang thai: {WebUtility.HtmlEncode(booking.BookingPaymentStatus)}</div>
                        </td>
                        <td align="right" style="padding:16px;">
                          <img src="cid:qr-boarding" width="140" height="140" alt="QR check-in" style="display:block;border:1px solid {Line};background:#FFFFFF;padding:6px;"/>
                          <div style="font-size:9px;color:{Muted};text-align:center;padding-top:4px;">Quet QR de check-in</div>
                        </td>
                      </tr>
                    </table>
                    <p style="margin:16px 0 0 0;padding-top:12px;border-top:1px solid {Line};font-size:11px;color:{Muted};">
                      Vui long xuat trinh ma QR (tren email nay hoac trong file PDF dinh kem) khi len tau.<br/>
                      Cam on ban da su dung dich vu Saigon Waterbus.
                    </p>
                  </td>
                </tr>
                <tr><td style="background:{Navy};padding:10px 24px;font-size:10px;color:#B8D7E4;" align="center">Saigon Waterbus &bull; waterbus</td></tr>
              </table>
            </div>
            """;
    }

    private static string BuildInfoCellHtml(string label, string? value) =>
        $"""
        <td style="padding:0 18px 0 0;">
          <div style="font-size:9px;font-weight:600;color:#7C8B95;">{WebUtility.HtmlEncode(label.ToUpperInvariant())}</div>
          <div style="font-size:13px;font-weight:bold;color:{Ink};">{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "-" : value)}</div>
        </td>
        """;

    private static byte[] BuildQrPngBytes(string qrToken)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(qrToken, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        return qrCode.GetGraphic(20);
    }

    private static string FormatVietnamTime(DateTimeOffset value) =>
        value.ToOffset(VietnamOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private sealed record InlineImage(string ContentId, byte[] Png);

    private async Task SendAsync(
        string email,
        string subject,
        string body,
        string? htmlBody,
        IReadOnlyList<EmailAttachment>? attachments,
        IReadOnlyList<InlineImage>? inlineImages,
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

        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            var htmlView = AlternateView.CreateAlternateViewFromString(
                htmlBody,
                Encoding.UTF8,
                MediaTypeNames.Text.Html);
            foreach (var image in inlineImages ?? [])
            {
                var resource = new LinkedResource(new MemoryStream(image.Png), "image/png")
                {
                    ContentId = image.ContentId,
                    TransferEncoding = TransferEncoding.Base64
                };
                htmlView.LinkedResources.Add(resource);
            }

            message.AlternateViews.Add(htmlView);
        }

        foreach (var attachment in attachments ?? [])
        {
            if (attachment.Content.Length == 0 || string.IsNullOrWhiteSpace(attachment.Name))
            {
                continue;
            }

            message.Attachments.Add(new Attachment(
                new MemoryStream(attachment.Content),
                attachment.Name,
                attachment.ContentType));
        }

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(options.Username, options.Password),
            Timeout = 30000
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
