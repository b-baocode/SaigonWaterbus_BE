using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class BrevoPaymentNotificationSender : IPaymentNotificationSender
{
    private const string HttpClientName = "Brevo";
    private const string CustomBookingType = "CustomBooking";
    private const string TicketQrImagePathPrefix = "/api/tickets/qr-image/";
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<BrevoOptions> _optionsMonitor;
    private readonly ILogger<BrevoPaymentNotificationSender> _logger;

    public BrevoPaymentNotificationSender(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<BrevoOptions> optionsMonitor,
        ILogger<BrevoPaymentNotificationSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task SendPaymentSucceededAsync(
        PaymentSucceededNotification notification,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey)
            || string.IsNullOrWhiteSpace(options.SenderEmail)
            || string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            _logger.LogWarning("Brevo payment notification is enabled but required configuration is missing.");
            return;
        }

        var payload = BuildPayload(options, notification);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.ApiBaseUrl.TrimEnd('/')}/smtp/email");
        request.Headers.TryAddWithoutValidation("api-key", options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Brevo payment notification sent. BookingCode: {BookingCode}, PaymentCode: {PaymentCode}, Email: {Email}",
                    notification.BookingCode,
                    notification.PaymentCode,
                    notification.Email);
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Brevo payment notification failed. Status: {StatusCode}, Body: {Body}, BookingCode: {BookingCode}, PaymentCode: {PaymentCode}, Email: {Email}",
                response.StatusCode,
                Truncate(body, 400),
                notification.BookingCode,
                notification.PaymentCode,
                notification.Email);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Brevo payment notification failed. BookingCode: {BookingCode}, PaymentCode: {PaymentCode}, Email: {Email}",
                notification.BookingCode,
                notification.PaymentCode,
                notification.Email);
        }
    }

    public async Task SendBoardingPassAsync(
        BoardingPassNotification notification,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey)
            || string.IsNullOrWhiteSpace(options.SenderEmail)
            || string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            _logger.LogWarning("Brevo boarding pass notification is enabled but required configuration is missing.");
            return;
        }

        var payload = BuildBoardingPassPayload(options, notification);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.ApiBaseUrl.TrimEnd('/')}/smtp/email");
        request.Headers.TryAddWithoutValidation("api-key", options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Brevo boarding pass notification sent. BookingCode: {BookingCode}, TicketCode: {TicketCode}, Email: {Email}",
                    notification.Booking.BookingCode,
                    notification.TicketCode,
                    notification.Booking.Email);
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Brevo boarding pass notification failed. Status: {StatusCode}, Body: {Body}, BookingCode: {BookingCode}, TicketCode: {TicketCode}, Email: {Email}",
                response.StatusCode,
                Truncate(body, 400),
                notification.Booking.BookingCode,
                notification.TicketCode,
                notification.Booking.Email);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Brevo boarding pass notification failed. BookingCode: {BookingCode}, TicketCode: {TicketCode}, Email: {Email}",
                notification.Booking.BookingCode,
                notification.TicketCode,
                notification.Booking.Email);
        }
    }

    private static Dictionary<string, object?> BuildPayload(
        BrevoOptions options,
        PaymentSucceededNotification notification)
    {
        var parameters = BuildTemplateParams(options, notification, boardingPass: null);
        return BuildPayload(
            options,
            notification.Email,
            notification.ContactName,
            ResolvePaymentTemplateId(options, notification),
            parameters,
            BuildSubject(notification),
            BuildHtmlContent(notification));
    }

    private static Dictionary<string, object?> BuildBoardingPassPayload(
        BrevoOptions options,
        BoardingPassNotification notification)
    {
        var parameters = BuildTemplateParams(options, notification.Booking, notification);
        return BuildPayload(
            options,
            notification.Booking.Email,
            notification.Booking.ContactName,
            ResolveBoardingPassTemplateId(options),
            parameters,
            $"Saigon Waterbus - Ve len tau {notification.Booking.BookingCode}",
            BuildHtmlContent(notification.Booking));
    }

    private static Dictionary<string, object?> BuildPayload(
        BrevoOptions options,
        string email,
        string contactName,
        int templateId,
        Dictionary<string, object?> parameters,
        string subject,
        string htmlContent)
    {
        var payload = new Dictionary<string, object?>
        {
            ["sender"] = new
            {
                email = options.SenderEmail,
                name = options.SenderName
            },
            ["to"] = new[] { new { email, name = contactName } },
            ["params"] = parameters
        };

        if (templateId > 0)
        {
            payload["templateId"] = templateId;
            return payload;
        }

        payload["subject"] = subject;
        payload["htmlContent"] = htmlContent;
        return payload;
    }

    private static int ResolvePaymentTemplateId(BrevoOptions options, PaymentSucceededNotification notification)
    {
        if (string.Equals(notification.BookingType, CustomBookingType, StringComparison.OrdinalIgnoreCase))
        {
            return FirstPositive(
                notification.IsFullyPaid ? options.PaymentFullTemplateId : options.PaymentDepositTemplateId,
                options.CustomBookingQuoteTemplateId,
                options.BookingPaymentConfirmationTemplateId);
        }

        return FirstPositive(
            notification.IsFullyPaid ? options.PaymentFullTemplateId : options.PaymentDepositTemplateId,
            options.BookingPaymentConfirmationTemplateId);
    }

    private static int ResolveBoardingPassTemplateId(BrevoOptions options) =>
        FirstPositive(
            options.CustomBookingConfirmationTemplateId,
            options.PaymentFullTemplateId,
            options.BookingPaymentConfirmationTemplateId);

    private static Dictionary<string, object?> BuildTemplateParams(
        BrevoOptions options,
        PaymentSucceededNotification notification,
        BoardingPassNotification? boardingPass)
    {
        var paymentAmount = FormatMoney(notification.PaymentAmount, notification.Currency);
        var bookingTotalAmount = FormatMoney(notification.BookingTotalAmount, notification.Currency);
        var depositAmount = FormatMoney(notification.DepositAmount, notification.Currency);
        var remainingAmount = FormatMoney(notification.RemainingAmount, notification.Currency);
        var paymentLabel = notification.IsFullyPaid ? "Thanh toán đủ" : "Đặt cọc";
        var totalPaidAmount = FormatMoney(notification.DepositAmount, notification.Currency);
        var paymentSummaryLabel = notification.IsFullyPaid ? "Đã thanh toán" : "Còn lại";
        var paymentSummaryAmount = notification.IsFullyPaid ? totalPaidAmount : remainingAmount;
        var departureText = notification.DepartureDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var timeline = ResolveTimeline(notification);
        var stops = notification.Stops
            .Select((stop, index) => new Dictionary<string, object?>
            {
                ["order"] = index + 1,
                ["name"] = stop.Name,
                ["description"] = string.IsNullOrWhiteSpace(stop.Description)
                    ? "Dừng theo lịch trình"
                    : stop.Description.Trim(),
                ["durationMinutes"] = stop.StayDurationMinutes,
                ["durationText"] = FormatDuration(stop.StayDurationMinutes)
            })
            .ToArray();
        var routeSummary = RouteSummary(notification);
        var qrPayload = boardingPass?.QrToken;
        var qrImageUrl = string.IsNullOrWhiteSpace(boardingPass?.QrImageUrl)
            ? CreateQrImageUrl(options.PublicApiBaseUrl, qrPayload)
            : boardingPass.QrImageUrl;

        return new Dictionary<string, object?>
        {
            ["requestCode"] = notification.BookingCode,
            ["status"] = notification.BookingPaymentStatus,
            ["statusLabel"] = boardingPass is null
                ? notification.IsFullyPaid ? "Đã xác nhận thanh toán" : "Đã xác nhận đặt cọc"
                : "Đã xác nhận",
            ["name"] = notification.ContactName,
            ["full_name"] = notification.ContactName,
            ["fullName"] = notification.ContactName,
            ["email"] = notification.Email,
            ["contactName"] = notification.ContactName,
            ["contactPhone"] = notification.ContactPhone,
            ["contactEmail"] = notification.Email,
            ["booking_code"] = notification.BookingCode,
            ["bookingCode"] = notification.BookingCode,
            ["booking_type"] = notification.BookingType,
            ["bookingType"] = notification.BookingType,
            ["bookingDate"] = FormatDateTimeOffset(notification.BookingCreatedAt, "dd/MM/yyyy"),
            ["confirmedAt"] = FormatDateTimeOffset(notification.PaidAt, "dd/MM/yyyy HH:mm"),
            ["payment_code"] = notification.PaymentCode,
            ["paymentCode"] = notification.PaymentCode,
            ["payment_purpose"] = notification.PaymentPurpose,
            ["paymentPurpose"] = notification.PaymentPurpose,
            ["payment_label"] = paymentLabel,
            ["paymentLabel"] = paymentLabel,
            ["payment_amount"] = paymentAmount,
            ["paymentAmount"] = paymentAmount,
            ["payment_amount_value"] = notification.PaymentAmount,
            ["booking_total_amount"] = bookingTotalAmount,
            ["bookingTotalAmount"] = bookingTotalAmount,
            ["quotedPrice"] = bookingTotalAmount,
            ["depositPercent"] = ResolveDepositPercent(notification),
            ["deposit_amount"] = depositAmount,
            ["depositAmount"] = depositAmount,
            ["remaining_amount"] = remainingAmount,
            ["remainingAmount"] = remainingAmount,
            ["paidAmount"] = totalPaidAmount,
            ["totalPaidAmount"] = totalPaidAmount,
            ["paymentSummaryLabel"] = paymentSummaryLabel,
            ["paymentSummaryAmount"] = paymentSummaryAmount,
            ["currency"] = notification.Currency,
            ["booking_payment_status"] = notification.BookingPaymentStatus,
            ["bookingPaymentStatus"] = notification.BookingPaymentStatus,
            ["paid_at"] = FormatDateTimeOffset(notification.PaidAt, "dd/MM/yyyy HH:mm"),
            ["paidAt"] = FormatDateTimeOffset(notification.PaidAt, "dd/MM/yyyy HH:mm"),
            ["is_fully_paid"] = notification.IsFullyPaid,
            ["isFullyPaid"] = notification.IsFullyPaid,
            ["departureDate"] = departureText,
            ["startTime"] = timeline.StartTime,
            ["timelineStartTime"] = timeline.StartTime,
            ["timelineEndTime"] = timeline.EndTime,
            ["estimatedEndDate"] = timeline.EndDate,
            ["durationText"] = ResolveBookingDurationText(notification),
            ["routeSummary"] = routeSummary,
            ["routeDetailText"] = routeSummary,
            ["routeTimelineText"] = routeSummary,
            ["fromStationName"] = ResolveText(notification.FromStationName),
            ["fromStationAddress"] = ResolveText(notification.FromStationAddress ?? notification.FromStationName),
            ["toStationName"] = ResolveText(notification.ToStationName),
            ["toStationAddress"] = ResolveText(notification.ToStationAddress ?? notification.ToStationName),
            ["vesselName"] = ResolveText(notification.BoatName, "Chưa gán tàu"),
            ["passengerCount"] = notification.PassengerCount.ToString(CultureInfo.InvariantCulture),
            ["STOPS"] = stops,
            ["ticketCode"] = boardingPass?.TicketCode,
            ["qrPayload"] = qrPayload,
            ["qrImageUrl"] = qrImageUrl,
            ["qrCodeUrl"] = qrImageUrl
        };
    }

    private static string BuildSubject(PaymentSucceededNotification notification) =>
        notification.IsFullyPaid
            ? $"Saigon Waterbus - Xác nhận thanh toán đủ {notification.BookingCode}"
            : $"Saigon Waterbus - Xác nhận đặt cọc {notification.BookingCode}";

    private static string BuildHtmlContent(PaymentSucceededNotification notification)
    {
        var paymentLabel = notification.IsFullyPaid ? "thanh toán đủ" : "đặt cọc";

        return
            "<html><body>"
            + $"<p>Xin chào {Encode(notification.ContactName)},</p>"
            + $"<p>Saigon Waterbus đã ghi nhận {paymentLabel} cho booking <strong>{Encode(notification.BookingCode)}</strong>.</p>"
            + "<ul>"
            + $"<li>Mã thanh toán: {Encode(notification.PaymentCode)}</li>"
            + $"<li>Loại thanh toán: {Encode(notification.PaymentPurpose)}</li>"
            + $"<li>Số tiền đã thanh toán: {Encode(FormatMoney(notification.PaymentAmount, notification.Currency))}</li>"
            + $"<li>Tổng giá trị booking: {Encode(FormatMoney(notification.BookingTotalAmount, notification.Currency))}</li>"
            + $"<li>Đã đặt cọc/đã thanh toán: {Encode(FormatMoney(notification.DepositAmount, notification.Currency))}</li>"
            + $"<li>Còn lại: {Encode(FormatMoney(notification.RemainingAmount, notification.Currency))}</li>"
            + $"<li>Trạng thái thanh toán: {Encode(notification.BookingPaymentStatus)}</li>"
            + $"<li>Thời gian ghi nhận: {notification.PaidAt:O}</li>"
            + "</ul>"
            + "<p>Cảm ơn bạn đã sử dụng dịch vụ Saigon Waterbus.</p>"
            + "</body></html>";
    }

    private static int FirstPositive(params int[] values) =>
        values.FirstOrDefault(value => value > 0);

    private static string FormatMoney(decimal amount, string currency) =>
        string.Create(ViCulture, $"{amount:N0} {currency}");

    private static string FormatDateTimeOffset(DateTimeOffset value, string format)
    {
        var displayValue = value == default ? DateTimeOffset.UtcNow : value;
        return displayValue.ToOffset(TimeSpan.FromHours(7)).ToString(format, CultureInfo.InvariantCulture);
    }

    private static string ResolveDepositPercent(PaymentSucceededNotification notification)
    {
        if (notification.BookingTotalAmount <= 0)
        {
            return "0";
        }

        var percent = notification.DepositAmount / notification.BookingTotalAmount * 100m;
        return percent.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string ResolveBookingDurationText(PaymentSucceededNotification notification)
    {
        if (notification.DurationValue <= 0)
        {
            return "Chưa xác định";
        }

        if (string.Equals(notification.RentalUnit, "Day", StringComparison.OrdinalIgnoreCase))
        {
            return $"{notification.DurationValue} ngày";
        }

        if (string.Equals(notification.RentalUnit, "Hour", StringComparison.OrdinalIgnoreCase))
        {
            return $"{notification.DurationValue} giờ";
        }

        return $"{notification.DurationValue} {notification.RentalUnit}";
    }

    private static string FormatDuration(int minutes)
    {
        if (minutes <= 0)
        {
            return "0 phút";
        }

        var hours = minutes / 60;
        var remainingMinutes = minutes % 60;
        if (hours <= 0)
        {
            return $"{remainingMinutes} phút";
        }

        return remainingMinutes == 0
            ? $"{hours} giờ"
            : $"{hours} giờ {remainingMinutes} phút";
    }

    private static (string StartTime, string EndTime, string? EndDate) ResolveTimeline(
        PaymentSucceededNotification notification)
    {
        var startTime = notification.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "Chưa xác định";
        if (!notification.DepartureDate.HasValue || !notification.StartTime.HasValue || notification.DurationValue <= 0)
        {
            return (startTime, "Chưa xác định", notification.DepartureDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        }

        var startDateTime = notification.DepartureDate.Value.ToDateTime(notification.StartTime.Value);
        var endDateTime = string.Equals(notification.RentalUnit, "Day", StringComparison.OrdinalIgnoreCase)
            ? startDateTime.AddDays(notification.DurationValue)
            : startDateTime.AddHours(notification.DurationValue);

        return (
            startTime,
            TimeOnly.FromDateTime(endDateTime).ToString("HH:mm", CultureInfo.InvariantCulture),
            DateOnly.FromDateTime(endDateTime).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
    }

    private static string RouteSummary(PaymentSucceededNotification notification)
    {
        var points = new List<string>();
        if (!string.IsNullOrWhiteSpace(notification.FromStationName))
        {
            points.Add(notification.FromStationName);
        }

        points.AddRange(notification.Stops.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)));

        if (!string.IsNullOrWhiteSpace(notification.ToStationName))
        {
            points.Add(notification.ToStationName);
        }

        return points.Count == 0 ? notification.BookingCode : string.Join(" -> ", points);
    }

    private static string? CreateQrImageUrl(string? publicApiBaseUrl, string? qrPayload)
    {
        if (string.IsNullOrWhiteSpace(publicApiBaseUrl) || string.IsNullOrWhiteSpace(qrPayload))
        {
            return null;
        }

        return $"{publicApiBaseUrl.TrimEnd('/')}{TicketQrImagePathPrefix}{Uri.EscapeDataString(qrPayload)}";
    }

    private static string ResolveText(string? value, string fallback = "Chưa xác định") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
}
