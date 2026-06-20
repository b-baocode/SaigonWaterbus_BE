using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Payments;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Payments;

public class PayOsCustomBookingPaymentGatewayTests
{
    [Test]
    public async Task CreateRefundPayoutUsesDedicatedPayoutCredentialsAndChecksumKey()
    {
        var httpHandler = new CapturingHttpMessageHandler();
        var gateway = new PayOsCustomBookingPaymentGateway(
            new TestHttpClientFactory(httpHandler),
            new TestOptionsMonitor<PayOsOptions>(CreatePayOsOptions()),
            NullLogger<PayOsCustomBookingPaymentGateway>.Instance);

        await gateway.CreateRefundPayoutAsync(
            new CustomBookingRefundPayoutRequest(
                "CBR-123",
                1000000,
                "Hoan tien SWB 1234ABCD",
                "970416",
                "22929167",
                "NGUYEN HUU HOANG",
                "idempotency-key"),
            CancellationToken.None);

        httpHandler.CapturedRequestUri.ShouldBe("https://payos.test/v1/payouts");
        httpHandler.CapturedHeaders["x-client-id"].ShouldBe("payout-client-id");
        httpHandler.CapturedHeaders["x-api-key"].ShouldBe("payout-api-key");
        httpHandler.CapturedHeaders["x-idempotency-key"].ShouldBe("idempotency-key");
        httpHandler.CapturedHeaders["x-signature"].ShouldBe(HmacSha256(
            "amount=1000000&category=%5B%22refund%22%2C%22custom-booking%22%5D&description=Hoan%20tien%20SWB%201234ABCD&referenceId=CBR-123&toAccountNumber=22929167&toBin=970416",
            "payout-checksum-key"));

        using var payload = JsonDocument.Parse(httpHandler.CapturedBody.ShouldNotBeNull());
        payload.RootElement.GetProperty("referenceId").GetString().ShouldBe("CBR-123");
        payload.RootElement.GetProperty("category")[0].GetString().ShouldBe("refund");
        payload.RootElement.GetProperty("category")[1].GetString().ShouldBe("custom-booking");
    }

    [Test]
    public async Task CreateRefundPayoutRequiresDedicatedPayoutCredentials()
    {
        var options = CreatePayOsOptions();
        options.PayoutClientId = null;
        var gateway = new PayOsCustomBookingPaymentGateway(
            new TestHttpClientFactory(new CapturingHttpMessageHandler()),
            new TestOptionsMonitor<PayOsOptions>(options),
            NullLogger<PayOsCustomBookingPaymentGateway>.Instance);

        var exception = await Should.ThrowAsync<PaymentGatewayException>(() =>
            gateway.CreateRefundPayoutAsync(
                new CustomBookingRefundPayoutRequest(
                    "CBR-123",
                    1000000,
                    "Hoan tien SWB 1234ABCD",
                    "970416",
                    "22929167",
                    "NGUYEN HUU HOANG",
                    "idempotency-key"),
                CancellationToken.None));

        exception.Message.ShouldBe("PayOS chưa cấu hình đủ PayoutClientId, PayoutApiKey và PayoutChecksumKey để hoàn tiền.");
    }

    [Test]
    public async Task CreateRefundPayoutRejectsInvalidPayOsDestinationBeforeHttpCall()
    {
        var httpHandler = new CapturingHttpMessageHandler();
        var gateway = new PayOsCustomBookingPaymentGateway(
            new TestHttpClientFactory(httpHandler),
            new TestOptionsMonitor<PayOsOptions>(CreatePayOsOptions()),
            NullLogger<PayOsCustomBookingPaymentGateway>.Instance);

        var exception = await Should.ThrowAsync<PaymentGatewayException>(() =>
            gateway.CreateRefundPayoutAsync(
                new CustomBookingRefundPayoutRequest(
                    "CBR-123",
                    1000000,
                    "Hoan tien SWB 1234ABCD",
                    "97041A",
                    "2292 9167",
                    "NGUYEN HUU HOANG",
                    "idempotency-key"),
                CancellationToken.None));

        exception.Message.ShouldBe("PayOS payout toBin phải gồm đúng 6 chữ số.");
        httpHandler.CapturedRequestUri.ShouldBeNull();
    }

    [Test]
    public async Task GetRefundPayoutByReferenceIdParsesOfficialPayOsArrayShape()
    {
        var httpHandler = new CapturingHttpMessageHandler
        {
            ResponseBody =
                """
                {
                  "code":"00",
                  "desc":"success",
                  "data":{
                    "payouts":[
                      {
                        "id":"payout-id",
                        "referenceId":"CBR-123",
                        "approvalState":"PROCESSING",
                        "transactions":[
                          {
                            "referenceId":"CBR-123",
                            "amount":1000000,
                            "state":"RECEIVED"
                          }
                        ]
                      }
                    ]
                  }
                }
                """
        };
        var gateway = new PayOsCustomBookingPaymentGateway(
            new TestHttpClientFactory(httpHandler),
            new TestOptionsMonitor<PayOsOptions>(CreatePayOsOptions()),
            NullLogger<PayOsCustomBookingPaymentGateway>.Instance);

        var result = await gateway.GetRefundPayoutByReferenceIdAsync("CBR-123", CancellationToken.None);

        httpHandler.CapturedRequestUri.ShouldBe("https://payos.test/v1/payouts?limit=1&offset=0&referenceId=CBR-123");
        result.ShouldNotBeNull();
        result.PayoutId.ShouldBe("payout-id");
        result.Status.ShouldBe("PROCESSING");
        result.ReferenceId.ShouldBe("CBR-123");
        result.Amount.ShouldBe(1000000);
    }

    private static PayOsOptions CreatePayOsOptions() =>
        new()
        {
            Enabled = true,
            ApiBaseUrl = "https://payos.test",
            ClientId = "payment-client-id",
            ApiKey = "payment-api-key",
            ChecksumKey = "payment-checksum-key",
            ReturnUrl = "https://app.test/payment-return",
            CancelUrl = "https://app.test/payment-cancel",
            PayoutClientId = "payout-client-id",
            PayoutApiKey = "payout-api-key",
            PayoutChecksumKey = "payout-checksum-key"
        };

    private static string HmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public string? CapturedRequestUri { get; private set; }

        public Dictionary<string, string> CapturedHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? CapturedBody { get; private set; }

        public string ResponseBody { get; init; } =
            """{"code":"00","desc":"success","data":{"id":"payout-id","referenceId":"CBR-123","approvalState":"Created","transactions":[]}}""";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedRequestUri = request.RequestUri?.ToString();
            foreach (var header in request.Headers)
            {
                CapturedHeaders[header.Key] = header.Value.Single();
            }

            CapturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
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
