using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class BrevoOtpSender : IOtpSender
{
    private const string HttpClientName = "Brevo";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<BrevoOptions> _optionsMonitor;
    private readonly IOtpPolicy _otpPolicy;
    private readonly ILogger<BrevoOtpSender> _logger;

    public BrevoOtpSender(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<BrevoOptions> optionsMonitor,
        IOtpPolicy otpPolicy,
        ILogger<BrevoOtpSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = optionsMonitor;
        _otpPolicy = otpPolicy;
        _logger = logger;
    }

    public async Task SendAsync(string email, string code, OtpPurpose purpose, string? recipientName, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            _logger.LogWarning("Brevo OTP integration is disabled. Skipping OTP send to {Email}", email);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new OtpDispatchException("Brevo ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.SenderEmail))
        {
            throw new OtpDispatchException("Brevo SenderEmail is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            throw new OtpDispatchException("Brevo ApiBaseUrl is not configured.");
        }

        var templateId = ResolveTemplateId(options, purpose);
        if (templateId <= 0)
        {
            throw new OtpDispatchException($"Brevo template id is not configured for purpose '{purpose}'.");
        }

        var payload = new
        {
            sender = new
            {
                email = options.SenderEmail,
                name = options.SenderName
            },
            to = new[]
            {
                new { email }
            },
            templateId,
            @params = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["otp_code"] = code,
                ["otpCode"] = code,
                ["name"] = recipientName,
                ["full_name"] = recipientName,
                ["fullName"] = recipientName,
                ["ttl_minutes"] = _otpPolicy.ExpirationMinutes,
                ["ttlMinutes"] = _otpPolicy.ExpirationMinutes,
                ["purpose"] = purpose.ToString(),
                ["email"] = email
            }
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.ApiBaseUrl.TrimEnd('/')}/smtp/email");
        request.Headers.TryAddWithoutValidation("api-key", options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var reason = response.ReasonPhrase ?? "Unknown";
                throw new OtpDispatchException(
                    $"Brevo OTP send failed ({(int)response.StatusCode} {reason}): {Truncate(body, 400)}");
            }

            _logger.LogInformation(
                "Brevo OTP sent successfully. Purpose: {Purpose}, Email: {Email}, MessageId: {MessageId}",
                purpose,
                email,
                TryGetMessageId(body) ?? "(n/a)");
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OtpDispatchException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(
                ex,
                "Brevo OTP send failed. Purpose: {Purpose}, Email: {Email}",
                purpose,
                email);
            throw new OtpDispatchException($"Unable to send OTP email via Brevo: {ex.Message}");
        }
    }

    private static int ResolveTemplateId(BrevoOptions options, OtpPurpose purpose) =>
        purpose switch
        {
            OtpPurpose.Register => options.RegisterTemplateId,
            OtpPurpose.ForgotPassword => options.ForgotPasswordTemplateId,
            _ => options.LoginTemplateId
        };

    private static string? TryGetMessageId(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("messageId", out var messageIdElement)
                && messageIdElement.ValueKind == JsonValueKind.String)
            {
                return messageIdElement.GetString();
            }

            return null;
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
