using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Auth;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

public class BrevoEmailFailureTests
{
    [Test]
    public async Task OtpSenderThrowsWhenBrevoRejectsRequest()
    {
        var sender = new BrevoOtpSender(
            new TestHttpClientFactory(new RejectingHttpMessageHandler()),
            new TestOptionsMonitor<BrevoOptions>(CreateBrevoOptions()),
            new TestOtpPolicy(),
            NullLogger<BrevoOtpSender>.Instance);

        var exception = await Should.ThrowAsync<OtpDispatchException>(() =>
            sender.SendAsync(
                "customer@example.test",
                "123456",
                OtpPurpose.Register,
                "Nguyen Van A",
                CancellationToken.None));

        exception.Message.ShouldContain("401");
    }

    [Test]
    public async Task LoginNotificationThrowsWhenBrevoRejectsRequest()
    {
        var sender = new BrevoLoginNotificationSender(
            new TestHttpClientFactory(new RejectingHttpMessageHandler()),
            new TestOptionsMonitor<BrevoOptions>(CreateBrevoOptions()),
            new TestOptionsMonitor<LoginNotificationOptions>(new LoginNotificationOptions
            {
                Enabled = true,
                TemplateId = 2
            }),
            NullLogger<BrevoLoginNotificationSender>.Instance);

        var exception = await Should.ThrowAsync<EmailDispatchException>(() =>
            sender.SendLoginSucceededAsync(
                new LoginNotification(
                    "customer@example.test",
                    "Nguyen Van A",
                    "Google",
                    DateTimeOffset.UtcNow,
                    "Test device"),
                CancellationToken.None));

        exception.Message.ShouldContain("401");
    }

    private static BrevoOptions CreateBrevoOptions() => new()
    {
        Enabled = true,
        ApiBaseUrl = "https://brevo.test",
        ApiKey = "test-api-key",
        SenderEmail = "noreply@example.test",
        TemplateId = 1,
        LoginTemplateId = 1
    };

    private sealed class RejectingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"message\":\"source IP is not authorized\"}")
            });
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestOtpPolicy : IOtpPolicy
    {
        public int ExpirationMinutes => 5;

        public int ResendSeconds => 60;

        public int MaxAttempts => 5;
    }
}
