using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class BrevoCustomBookingConfirmationEmailSender : ICustomBookingConfirmationEmailSender
{
    private const string HttpClientName = "Brevo";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApplicationDbContext _context;
    private readonly IOptionsMonitor<BrevoOptions> _optionsMonitor;
    private readonly ILogger<BrevoCustomBookingConfirmationEmailSender> _logger;

    public BrevoCustomBookingConfirmationEmailSender(
        IHttpClientFactory httpClientFactory,
        IApplicationDbContext context,
        IOptionsMonitor<BrevoOptions> optionsMonitor,
        ILogger<BrevoCustomBookingConfirmationEmailSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _context = context;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task SendConfirmationAsync(
        CustomBookingRequest request,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            _logger.LogInformation("Brevo is disabled. Skipping custom booking confirmation email for {RequestId}.", request.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ContactEmail))
        {
            _logger.LogInformation("Custom booking request {RequestId} has no contact email. Skipping confirmation email.", request.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey)
            || string.IsNullOrWhiteSpace(options.SenderEmail)
            || string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            _logger.LogWarning("Brevo is not fully configured. Skipping custom booking confirmation email for {RequestId}.", request.Id);
            return;
        }

        if (options.CustomBookingConfirmationTemplateId <= 0)
        {
            _logger.LogWarning(
                "Brevo CustomBookingConfirmationTemplateId is not configured. Skipping custom booking confirmation email for {RequestId}.",
                request.Id);
            return;
        }

        try
        {
            var routeSegments = await LoadRouteSegmentsAsync(request, cancellationToken);
            var payload = new
            {
                sender = new { email = options.SenderEmail, name = options.SenderName },
                to = new[] { new { email = request.ContactEmail, name = request.ContactName } },
                templateId = options.CustomBookingConfirmationTemplateId,
                @params = CustomBookingEmailParamsFactory.CreateConfirmationParams(request, routeSegments)
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
                    "Brevo custom booking confirmation email failed. RequestId: {RequestId}, Status: {StatusCode}, Body: {Body}",
                    request.Id,
                    (int)response.StatusCode,
                    Truncate(body, 400));
                return;
            }

            _logger.LogInformation(
                "Custom booking confirmation email sent by Brevo. RequestId: {RequestId}, Email: {Email}, TemplateId: {TemplateId}, MessageId: {MessageId}",
                request.Id,
                request.ContactEmail,
                options.CustomBookingConfirmationTemplateId,
                TryGetMessageId(body) ?? "(n/a)");
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Brevo custom booking confirmation email failed. RequestId: {RequestId}, Email: {Email}",
                request.Id,
                request.ContactEmail);
        }
    }

    private async Task<IReadOnlyList<RouteSegment>> LoadRouteSegmentsAsync(
        CustomBookingRequest request,
        CancellationToken cancellationToken)
    {
        var stationIds = GetStationIds(request).Distinct().ToArray();
        if (stationIds.Length < 2)
        {
            return Array.Empty<RouteSegment>();
        }

        return await _context.Set<RouteSegment>()
            .Where(x => stationIds.Contains(x.FromStationId) && stationIds.Contains(x.ToStationId))
            .OrderBy(x => x.SegmentOrder)
            .ToArrayAsync(cancellationToken);
    }

    private static IEnumerable<Guid> GetStationIds(CustomBookingRequest request)
    {
        if (request.FromStationId.HasValue)
        {
            yield return request.FromStationId.Value;
        }

        foreach (var stop in request.ItineraryStops)
        {
            yield return stop.StationId;
        }

        if (request.ToStationId.HasValue)
        {
            yield return request.ToStationId.Value;
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
