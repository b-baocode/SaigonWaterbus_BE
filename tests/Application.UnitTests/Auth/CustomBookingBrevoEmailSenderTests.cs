using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Auth;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

[TestFixture]
public class CustomBookingBrevoEmailSenderTests
{
    [Test]
    public async Task QuoteEmailUsesTemplate14AndNeverIncludesQrParams()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var request = CreateCustomBookingRequest();
        request.Status = CustomBookingRequestStatus.Quoted;
        request.Tickets.Add(CreateActiveTicket(request));
        var httpHandler = new CapturingHttpMessageHandler();
        var sender = new BrevoCustomBookingQuoteEmailSender(
            new TestHttpClientFactory(httpHandler),
            context,
            OptionsMonitor(quoteTemplateId: 14, confirmationTemplateId: 13),
            NullLogger<BrevoCustomBookingQuoteEmailSender>.Instance);

        await sender.SendQuoteAsync(request, CancellationToken.None);

        httpHandler.CapturedRequestUri.ShouldBe("https://brevo.test/smtp/email");
        using var payload = JsonDocument.Parse(httpHandler.CapturedBody.ShouldNotBeNull());
        var root = payload.RootElement;
        root.GetProperty("templateId").GetInt32().ShouldBe(14);
        root.GetProperty("params").GetProperty("statusLabel").GetString().ShouldBe("Đã báo giá");
        root.GetProperty("params").TryGetProperty("logoUrl", out _).ShouldBeFalse();
        root.GetProperty("params").GetProperty("ticketCode").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("params").GetProperty("qrPayload").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("params").GetProperty("qrImageUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("params").GetProperty("qrCodeUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("params").GetProperty("paymentSummaryLabel").GetString().ShouldBe("Còn lại");
        root.GetProperty("params").GetProperty("paymentSummaryAmount").GetString().ShouldBe("2.500.000 VND");
        root.GetProperty("params").GetProperty("baseVesselPrice").GetString().ShouldBe("4.600.000 VND");
        root.GetProperty("params").GetProperty("serviceFeeAmount").GetString().ShouldBe("400.000 VND");
        root.GetProperty("params").GetProperty("hasServiceFee").GetBoolean().ShouldBeTrue();
        root.GetProperty("params").GetProperty("specialRequests").GetString().ShouldBe("Trang trí sinh nhật");
        root.GetProperty("params").GetProperty("paidAmount").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("params").GetProperty("totalPaidAmount").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task ConfirmationEmailUsesTemplate13AndNeverIncludesQrParams()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var request = CreateCustomBookingRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        request.PassengerManifestStatus = PassengerManifestStatus.Completed;
        request.Tickets.Add(CreateActiveTicket(request));
        var httpHandler = new CapturingHttpMessageHandler();
        var sender = new BrevoCustomBookingConfirmationEmailSender(
            new TestHttpClientFactory(httpHandler),
            context,
            OptionsMonitor(quoteTemplateId: 14, confirmationTemplateId: 13),
            NullLogger<BrevoCustomBookingConfirmationEmailSender>.Instance);

        await sender.SendConfirmationAsync(request, CancellationToken.None);

        httpHandler.CapturedRequestUri.ShouldBe("https://brevo.test/smtp/email");
        using var payload = JsonDocument.Parse(httpHandler.CapturedBody.ShouldNotBeNull());
        var root = payload.RootElement;
        var parameters = root.GetProperty("params");
        root.GetProperty("templateId").GetInt32().ShouldBe(13);
        parameters.GetProperty("statusLabel").GetString().ShouldBe("Đã chốt thành công");
        parameters.TryGetProperty("logoUrl", out _).ShouldBeFalse();
        parameters.GetProperty("ticketCode").GetString().ShouldBe("CBT-TEST");
        parameters.GetProperty("qrPayload").ValueKind.ShouldBe(JsonValueKind.Null);
        parameters.GetProperty("qrImageUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        parameters.GetProperty("qrCodeUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        parameters.GetProperty("paymentSummaryLabel").GetString().ShouldBe("Đã thanh toán đặt cọc");
        parameters.GetProperty("paymentSummaryAmount").GetString().ShouldBe("2.500.000 VND");
        parameters.GetProperty("paidAmount").GetString().ShouldBe("2.500.000 VND");
        parameters.GetProperty("totalPaidAmount").GetString().ShouldBe("2.500.000 VND");
        parameters.GetProperty("baseVesselPrice").GetString().ShouldBe("4.600.000 VND");
        parameters.GetProperty("serviceFeeAmount").GetString().ShouldBe("400.000 VND");
        parameters.GetProperty("hasServiceFee").GetBoolean().ShouldBeTrue();
        parameters.GetProperty("specialRequests").GetString().ShouldBe("Trang trí sinh nhật");
    }

    private static CustomBookingRequest CreateCustomBookingRequest()
    {
        var request = new CustomBookingRequest
        {
            ContactName = "Nguyen Van A",
            ContactPhone = "+84900000000",
            ContactEmail = "customer@gmail.com",
            RequestedNumberOfDecks = 2,
            RequestedSeatSetupType = SeatSetupType.StandardAndVip,
            RentalUnit = VesselRentalUnit.Day,
            DepartureDate = new DateOnly(2026, 6, 20),
            PreferredStartTime = new TimeOnly(8, 30),
            PreferredEndTime = new TimeOnly(11, 30),
            EstimatedEndDate = new DateOnly(2026, 6, 20),
            EstimatedDurationMinutes = 180,
            FromLocation = "Bach Dang",
            ToLocation = "Linh Dong",
            PassengerCount = 2,
            AdultCount = 2,
            ChildCount = 0,
            SpecialRequests = "Trang trí sinh nhật",
            QuoteAcceptedAt = new DateTimeOffset(2026, 6, 18, 2, 0, 0, TimeSpan.Zero)
        };
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 5_000_000m,
            ServiceFeeAmount = 400_000m,
            DepositPercent = 50m,
            DepositAmount = 2_500_000m,
            RemainingAmount = 2_500_000m,
            Currency = "VND",
            ValidUntil = new DateTimeOffset(2026, 6, 19, 2, 0, 0, TimeSpan.Zero)
        };

        return request;
    }

    private static CustomBookingTicket CreateActiveTicket(CustomBookingRequest request) =>
        new()
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            TicketCode = "CBT-TEST",
            QrToken = "test-token",
            QrTokenHash = "test-token-hash",
            QrIssuedAt = new DateTimeOffset(2026, 6, 18, 3, 0, 0, TimeSpan.Zero),
            Status = CustomBookingTicketStatus.Active
        };

    private static TestOptionsMonitor<BrevoOptions> OptionsMonitor(
        int quoteTemplateId,
        int confirmationTemplateId) =>
        new(new BrevoOptions
        {
            Enabled = true,
            ApiBaseUrl = "https://brevo.test",
            ApiKey = "test-api-key",
            SenderEmail = "noreply@saigonwaterbus.test",
            SenderName = "Saigon Waterbus",
            CustomBookingQuoteTemplateId = quoteTemplateId,
            CustomBookingConfirmationTemplateId = confirmationTemplateId
        });

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public string? CapturedRequestUri { get; private set; }

        public string? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedRequestUri = request.RequestUri?.ToString();
            CapturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"messageId":"test-message-id"}""")
            };
        }
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
