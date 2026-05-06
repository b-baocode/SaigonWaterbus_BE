using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class BrevoLoginNotificationSender : ILoginNotificationSender
{
    private const string HttpClientName = "Brevo";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<BrevoOptions> _brevoOptionsMonitor;
    private readonly IOptionsMonitor<LoginNotificationOptions> _notificationOptionsMonitor;
    private readonly ILogger<BrevoLoginNotificationSender> _logger;

    public BrevoLoginNotificationSender(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<BrevoOptions> brevoOptionsMonitor,
        IOptionsMonitor<LoginNotificationOptions> notificationOptionsMonitor,
        ILogger<BrevoLoginNotificationSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _brevoOptionsMonitor = brevoOptionsMonitor;
        _notificationOptionsMonitor = notificationOptionsMonitor;
        _logger = logger;
    }

    public async Task SendLoginSucceededAsync(LoginNotification notification, CancellationToken cancellationToken)
    {
        var notificationOptions = _notificationOptionsMonitor.CurrentValue;
        var brevoOptions = _brevoOptionsMonitor.CurrentValue;

        if (!notificationOptions.Enabled || !brevoOptions.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(brevoOptions.ApiKey)
            || string.IsNullOrWhiteSpace(brevoOptions.SenderEmail)
            || string.IsNullOrWhiteSpace(brevoOptions.ApiBaseUrl)
            || notificationOptions.TemplateId <= 0)
        {
            _logger.LogWarning("Brevo login notification is enabled but required configuration is missing.");
            return;
        }

        var payload = new
        {
            sender = new
            {
                email = brevoOptions.SenderEmail,
                name = brevoOptions.SenderName
            },
            to = new[] { new { email = notification.Email, name = notification.FullName } },
            templateId = notificationOptions.TemplateId,
            @params = new Dictionary<string, object?>
            {
                ["name"] = notification.FullName,
                ["provider"] = notification.Provider,
                ["logged_in_at"] = notification.LoggedInAt,
                ["device"] = notification.DeviceInfo,
                ["web_name"] = notificationOptions.WebName
            }
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{brevoOptions.ApiBaseUrl.TrimEnd('/')}/smtp/email");
        request.Headers.TryAddWithoutValidation("api-key", brevoOptions.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Brevo login notification failed. Status: {StatusCode}, Body: {Body}",
                response.StatusCode,
                body);
        }
    }
}
