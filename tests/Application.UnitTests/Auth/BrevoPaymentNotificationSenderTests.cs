using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Auth;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

[TestFixture]
public class BrevoPaymentNotificationSenderTests
{
    [Test]
    public async Task PaymentSucceededUsesTemplate14AndDoesNotIncludeQr()
    {
        var httpHandler = new CapturingHttpMessageHandler();
        var sender = CreateSender(httpHandler);

        await sender.SendPaymentSucceededAsync(CreateNotification(isFullyPaid: false), CancellationToken.None);

        using var payload = JsonDocument.Parse(httpHandler.CapturedBody.ShouldNotBeNull());
        var root = payload.RootElement;
        var parameters = root.GetProperty("params");
        root.GetProperty("templateId").GetInt32().ShouldBe(14);
        parameters.GetProperty("ticketCode").ValueKind.ShouldBe(JsonValueKind.Null);
        parameters.GetProperty("qrImageUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        parameters.GetProperty("pdfUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        parameters.GetProperty("paymentSummaryLabel").GetString().ShouldBe("Còn lại");
    }

    [Test]
    public async Task BoardingPassUsesTemplate13AndIncludesQr()
    {
        var httpHandler = new CapturingHttpMessageHandler();
        var sender = CreateSender(httpHandler);
        var booking = CreateNotification(isFullyPaid: true);

        await sender.SendBoardingPassAsync(
            new BoardingPassNotification(
                booking,
                "TK123",
                "qr-token",
                PassengerName: "Tran Thi B",
                Attachments:
                [
                    new EmailAttachment("boarding-pass.pdf", "application/pdf", [1, 2, 3])
                ]),
            CancellationToken.None);

        using var payload = JsonDocument.Parse(httpHandler.CapturedBody.ShouldNotBeNull());
        var root = payload.RootElement;
        var parameters = root.GetProperty("params");
        var attachment = root.GetProperty("attachment")[0];
        root.GetProperty("templateId").GetInt32().ShouldBe(13);
        parameters.GetProperty("ticketCode").GetString().ShouldBe("TK123");
        parameters.GetProperty("qrPayload").GetString().ShouldBe("qr-token");
        parameters.GetProperty("qrImageUrl").GetString().ShouldBe("https://api.test/api/tickets/qr-image/qr-token");
        parameters.GetProperty("pdfUrl").GetString().ShouldBe("https://api.test/api/charter-bookings/tickets/pdf/qr-token");
        parameters.GetProperty("passengerName").GetString().ShouldBe("Tran Thi B");
        parameters.GetProperty("paymentSummaryLabel").GetString().ShouldBe("Đã thanh toán");
        attachment.GetProperty("name").GetString().ShouldBe("boarding-pass.pdf");
        attachment.GetProperty("content").GetString().ShouldBe("AQID");
    }

    [Test]
    public async Task ETicketUsesTemplate15AndIncludesQrUrlAliases()
    {
        var httpHandler = new CapturingHttpMessageHandler();
        var sender = CreateSender(httpHandler);
        var booking = CreateNotification(isFullyPaid: true);

        await sender.SendETicketsAsync(
            new ETicketNotification(
                booking,
                BookingQrToken: "booking-qr",
                TripCode: "TR-001",
                RouteName: "Bach Dang - Linh Dong",
                DepartureTime: new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)),
                ArrivalTime: new DateTimeOffset(2030, 1, 1, 8, 30, 0, TimeSpan.FromHours(7)),
                FromStationName: "Bach Dang",
                ToStationName: "Linh Dong",
                Tickets:
                [
                    new ETicketPassenger(
                        "Tran Thi B",
                        "A1",
                        "Nguoi lon",
                        "TK123",
                        "ticket-qr",
                        "passenger@example.com")
                ]),
            CancellationToken.None);

        using var payload = JsonDocument.Parse(httpHandler.CapturedBody.ShouldNotBeNull());
        var root = payload.RootElement;
        var parameters = root.GetProperty("params");
        var ticket = parameters.GetProperty("TICKETS")[0];
        root.GetProperty("templateId").GetInt32().ShouldBe(15);
        parameters.GetProperty("bookingQrPayload").GetString().ShouldBe("booking-qr");
        parameters.GetProperty("bookingQrImageUrl").GetString().ShouldBe("https://api.test/api/tickets/qr-image/booking-qr");
        parameters.GetProperty("bookingQrCodeUrl").GetString().ShouldBe("https://api.test/api/tickets/qr-image/booking-qr");
        parameters.GetProperty("bookingQrUrl").GetString().ShouldBe("https://api.test/api/tickets/qr-image/booking-qr");
        parameters.GetProperty("qrImageUrl").GetString().ShouldBe("https://api.test/api/tickets/qr-image/booking-qr");
        parameters.GetProperty("qrCodeUrl").GetString().ShouldBe("https://api.test/api/tickets/qr-image/booking-qr");
        ticket.GetProperty("qrPayload").GetString().ShouldBe("ticket-qr");
        ticket.GetProperty("qrImageUrl").GetString().ShouldBe("https://api.test/api/tickets/qr-image/ticket-qr");
        ticket.GetProperty("qrCodeUrl").GetString().ShouldBe("https://api.test/api/tickets/qr-image/ticket-qr");
        ticket.GetProperty("ticketQrImageUrl").GetString().ShouldBe("https://api.test/api/tickets/qr-image/ticket-qr");
        ticket.GetProperty("ticketQrCodeUrl").GetString().ShouldBe("https://api.test/api/tickets/qr-image/ticket-qr");
    }

    [Test]
    public async Task ETicketFallsBackToAzureWebsiteHostnameForQrImageUrls()
    {
        var previousPublicApiBaseUrl = Environment.GetEnvironmentVariable("PUBLIC_API_BASE_URL");
        var previousWebsiteHostname = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
        try
        {
            Environment.SetEnvironmentVariable("PUBLIC_API_BASE_URL", null);
            Environment.SetEnvironmentVariable("WEBSITE_HOSTNAME", "waterbus-api.azurewebsites.net");
            var httpHandler = new CapturingHttpMessageHandler();
            var sender = CreateSender(httpHandler, publicApiBaseUrl: "");
            var booking = CreateNotification(isFullyPaid: true);

            await sender.SendETicketsAsync(
                new ETicketNotification(
                    booking,
                    BookingQrToken: "booking-qr",
                    TripCode: "TR-001",
                    RouteName: "Bach Dang - Linh Dong",
                    DepartureTime: null,
                    ArrivalTime: null,
                    FromStationName: "Bach Dang",
                    ToStationName: "Linh Dong",
                    Tickets:
                    [
                        new ETicketPassenger(
                            "Tran Thi B",
                            "A1",
                            "Nguoi lon",
                            "TK123",
                            "ticket-qr",
                            "passenger@example.com")
                    ]),
                CancellationToken.None);

            using var payload = JsonDocument.Parse(httpHandler.CapturedBody.ShouldNotBeNull());
            var parameters = payload.RootElement.GetProperty("params");
            parameters.GetProperty("bookingQrImageUrl").GetString()
                .ShouldBe("https://waterbus-api.azurewebsites.net/api/tickets/qr-image/booking-qr");
            parameters.GetProperty("TICKETS")[0].GetProperty("qrImageUrl").GetString()
                .ShouldBe("https://waterbus-api.azurewebsites.net/api/tickets/qr-image/ticket-qr");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PUBLIC_API_BASE_URL", previousPublicApiBaseUrl);
            Environment.SetEnvironmentVariable("WEBSITE_HOSTNAME", previousWebsiteHostname);
        }
    }

    private static BrevoPaymentNotificationSender CreateSender(
        CapturingHttpMessageHandler httpHandler,
        string? publicApiBaseUrl = "https://api.test") =>
        new(
            new TestHttpClientFactory(httpHandler),
            new TestOptionsMonitor<BrevoOptions>(new BrevoOptions
            {
                Enabled = true,
                ApiBaseUrl = "https://brevo.test",
                ApiKey = "test-api-key",
                SenderEmail = "noreply@saigonwaterbus.test",
                SenderName = "Saigon Waterbus",
                PublicApiBaseUrl = publicApiBaseUrl,
                CharterBookingQuoteTemplateId = 14,
                CharterBookingConfirmationTemplateId = 13,
                PaymentDepositTemplateId = 14,
                PaymentFullTemplateId = 14,
                ETicketTemplateId = 15
            }),
            NullLogger<BrevoPaymentNotificationSender>.Instance);

    private static PaymentSucceededNotification CreateNotification(bool isFullyPaid) =>
        new(
            "customer@gmail.com",
            "Nguyen Van A",
            "0900000000",
            isFullyPaid ? "CB-FULL" : "CB-DEPOSIT",
            "CharterBooking",
            new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero),
            isFullyPaid ? "1000002" : "1000001",
            isFullyPaid ? "Full" : "Deposit",
            isFullyPaid ? 10_000m : 5_000m,
            "VND",
            10_000m,
            isFullyPaid ? "Paid" : "DepositPaid",
            isFullyPaid ? 10_000m : 5_000m,
            isFullyPaid ? 0m : 5_000m,
            new DateTimeOffset(2026, 6, 18, 2, 0, 0, TimeSpan.Zero),
            isFullyPaid,
            new DateOnly(2030, 1, 1),
            new TimeOnly(8, 30),
            "Hour",
            3,
            2,
            "Boat 01",
            "Bach Dang",
            "Bach Dang Address",
            "Linh Dong",
            "Linh Dong Address",
            [new PaymentNotificationStop("Thanh Da", "Tham quan", 30)]);

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public string? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
