using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Options;

namespace SaigonWaterbus.Infrastructure.Incidents;

public sealed class HttpIncidentGpsHookNotifier : IIncidentGpsHookNotifier
{
    private const string HttpClientName = "IncidentGpsHook";
    private const string SecretHeaderName = "X-Live-Hook-Secret";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<IncidentGpsHookOptions> _optionsMonitor;
    private readonly ILogger<HttpIncidentGpsHookNotifier> _logger;

    public HttpIncidentGpsHookNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<IncidentGpsHookOptions> optionsMonitor,
        ILogger<HttpIncidentGpsHookNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task NotifyAsync(
        IncidentGpsHookNotification notification,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.Secret))
        {
            _logger.LogWarning("Skipping incident GPS hook because BaseUrl or Secret is missing.");
            return;
        }

        var endpoint = $"{options.BaseUrl.TrimEnd('/')}/api/incidents/hook";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation(SecretHeaderName, options.Secret);
        request.Content = JsonContent.Create(notification, options: JsonOptions);

        try
        {
            using var response = await _httpClientFactory
                .CreateClient(HttpClientName)
                .SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Incident GPS hook failed. Event: {Event}, IncidentId: {IncidentId}, Status: {StatusCode}, Body: {Body}",
                    notification.Event,
                    notification.IncidentId,
                    (int)response.StatusCode,
                    Truncate(body, 500));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Incident GPS hook request failed. Event: {Event}, IncidentId: {IncidentId}",
                notification.Event,
                notification.IncidentId);
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value[..maxLength];
    }
}
