using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class BrevoLoginNotificationSender : ILoginNotificationSender
{
    private const string HttpClientName = "Brevo";
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");
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
            throw new EmailDispatchException("Brevo login notification configuration is incomplete.");
        }

        var recipientEmail = EmailRecipientResolver.Resolve(brevoOptions.TestRecipientEmail, notification.Email);
        var isRedirected = EmailRecipientResolver.IsRedirected(brevoOptions.TestRecipientEmail, notification.Email);
        var loginTime = notification.LoggedInAt
            .ToOffset(TimeSpan.FromHours(7))
            .ToString("dd/MM/yyyy HH:mm", ViCulture);
        var deviceInfo = string.IsNullOrWhiteSpace(notification.DeviceInfo)
            ? "Google Sign-In"
            : notification.DeviceInfo.Trim();
        var payload = new
        {
            sender = new
            {
                email = brevoOptions.SenderEmail,
                name = brevoOptions.SenderName
            },
            to = new[] { new { email = recipientEmail, name = notification.FullName } },
            templateId = notificationOptions.TemplateId,
            @params = new Dictionary<string, object?>
            {
                ["name"] = notification.FullName,
                ["username"] = notification.FullName,
                ["userName"] = notification.FullName,
                ["email"] = notification.Email,
                ["provider"] = notification.Provider,
                ["logged_in_at"] = notification.LoggedInAt,
                ["login_time"] = loginTime,
                ["loginTime"] = loginTime,
                ["device"] = deviceInfo,
                ["device_info"] = deviceInfo,
                ["deviceInfo"] = deviceInfo,
                ["web_name"] = notificationOptions.WebName,
                ["recipientEmail"] = recipientEmail,
                ["originalRecipientEmail"] = notification.Email,
                ["isTestRecipientRedirect"] = isRedirected,
                ["testRecipientEmail"] = isRedirected ? recipientEmail : null
            }
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{brevoOptions.ApiBaseUrl.TrimEnd('/')}/smtp/email");
        request.Headers.TryAddWithoutValidation("api-key", brevoOptions.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Brevo login notification failed. Status: {StatusCode}, Body: {Body}, Email: {Email}, RecipientEmail: {RecipientEmail}",
                    response.StatusCode,
                    body,
                    notification.Email,
                    recipientEmail);
                throw new EmailDispatchException(
                    $"Brevo login notification send failed ({(int)response.StatusCode} {response.ReasonPhrase ?? "Unknown"}).");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (EmailDispatchException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Brevo login notification failed for {Email}.", notification.Email);
            throw new EmailDispatchException($"Unable to send login notification via Brevo: {ex.Message}", ex);
        }
    }
}
