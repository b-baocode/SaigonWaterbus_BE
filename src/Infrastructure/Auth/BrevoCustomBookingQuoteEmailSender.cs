using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class BrevoCustomBookingQuoteEmailSender : ICustomBookingQuoteEmailSender
{
    private const string HttpClientName = "Brevo";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<BrevoOptions> _optionsMonitor;
    private readonly ILogger<BrevoCustomBookingQuoteEmailSender> _logger;

    public BrevoCustomBookingQuoteEmailSender(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<BrevoOptions> optionsMonitor,
        ILogger<BrevoCustomBookingQuoteEmailSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task SendQuoteAsync(CustomBookingRequest request, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            _logger.LogInformation("Brevo is disabled. Skipping custom booking quote email for {RequestId}.", request.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ContactEmail))
        {
            _logger.LogInformation("Custom booking request {RequestId} has no contact email. Skipping quote email.", request.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey)
            || string.IsNullOrWhiteSpace(options.SenderEmail)
            || string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            _logger.LogWarning("Brevo is not fully configured. Skipping custom booking quote email for {RequestId}.", request.Id);
            return;
        }

        try
        {
            var payload = new
            {
                sender = new
                {
                    email = options.SenderEmail,
                    name = options.SenderName
                },
                to = new[]
                {
                    new { email = request.ContactEmail, name = request.ContactName }
                },
                subject = CustomBookingQuoteEmailContentFactory.Subject(request),
                htmlContent = CustomBookingQuoteEmailContentFactory.Html(request),
                textContent = CustomBookingQuoteEmailContentFactory.PlainText(request)
            };

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{options.ApiBaseUrl.TrimEnd('/')}/smtp/email");
            httpRequest.Headers.TryAddWithoutValidation("api-key", options.ApiKey);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Content = JsonContent.Create(payload);

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Brevo custom booking quote email failed. RequestId: {RequestId}, Status: {StatusCode}, Body: {Body}",
                    request.Id,
                    (int)response.StatusCode,
                    Truncate(body, 400));
                return;
            }

            _logger.LogInformation(
                "Custom booking quote email sent by Brevo. RequestId: {RequestId}, Email: {Email}, MessageId: {MessageId}",
                request.Id,
                request.ContactEmail,
                TryGetMessageId(body) ?? "(n/a)");
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brevo custom booking quote email failed. RequestId: {RequestId}, Email: {Email}", request.Id, request.ContactEmail);
        }
    }

    private static string? TryGetMessageId(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.TryGetProperty("messageId", out var messageIdElement)
                && messageIdElement.ValueKind == JsonValueKind.String
                ? messageIdElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
