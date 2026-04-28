using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class EsmsSmsSender
{
    private const string HttpClientName = "Esms";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<EsmsOptions> _optionsMonitor;
    private readonly ILogger<EsmsSmsSender> _logger;

    public EsmsSmsSender(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<EsmsOptions> optionsMonitor,
        ILogger<EsmsSmsSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task<EsmsSendResult> SendAsync(EsmsSendRequest request, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            throw new InvalidOperationException("eSMS integration is disabled.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            throw new InvalidOperationException("eSMS ApiBaseUrl is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.EndpointPath))
        {
            throw new InvalidOperationException("eSMS EndpointPath is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new InvalidOperationException("eSMS ApiKey/SecretKey is not configured.");
        }

        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString("D")
            : request.RequestId.Trim();

        var payload = new Dictionary<string, string?>
        {
            ["ApiKey"] = options.ApiKey,
            ["SecretKey"] = options.SecretKey,
            ["Phone"] = request.Phone.Trim(),
            ["Content"] = request.Content.Trim(),
            ["Brandname"] = ResolveValue(request.Brandname, options.Brandname),
            ["SmsType"] = ResolveValue(request.SmsType, options.SmsType),
            ["IsUnicode"] = ResolveValue(request.IsUnicode, options.IsUnicode),
            ["Sandbox"] = ResolveValue(request.Sandbox, options.Sandbox),
            ["RequestId"] = requestId,
            ["campaignid"] = ResolveValue(request.CampaignId, options.CampaignId),
            ["CallbackUrl"] = ResolveValue(request.CallbackUrl, options.CallbackUrl)
        };

        var requestPayload = payload
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            BuildEndpoint(options.ApiBaseUrl, options.EndpointPath));
        message.Content = JsonContent.Create(requestPayload);

        using var response = await client.SendAsync(message, cancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"eSMS HTTP request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {Truncate(rawBody, 400)}");
        }

        var parsed = ParseResult(rawBody);
        var result = parsed with { RequestId = requestId };

        _logger.LogInformation(
            "eSMS response received. Phone: {Phone}, RequestId: {RequestId}, CodeResult: {CodeResult}, SmsId: {SmsId}",
            request.Phone,
            requestId,
            result.CodeResult ?? "(n/a)",
            result.SmsId ?? "(n/a)");

        return result;
    }

    private static EsmsSendResult ParseResult(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return new EsmsSendResult(string.Empty, null, null, "Empty response from eSMS.", null, rawBody);
        }

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            return new EsmsSendResult(
                RequestId: string.Empty,
                CodeResult: GetString(root, "CodeResult"),
                SmsId: GetString(root, "SMSID"),
                ErrorMessage: GetString(root, "ErrorMessage") ?? GetString(root, "error"),
                CountRegenerate: GetInt(root, "CountRegenerate"),
                RawResponse: rawBody);
        }
        catch (JsonException)
        {
            return new EsmsSendResult(string.Empty, null, null, "Unable to parse eSMS response JSON.", null, rawBody);
        }
    }

    private static string BuildEndpoint(string baseUrl, string endpointPath)
    {
        var sanitizedBaseUrl = baseUrl.TrimEnd('/');
        var sanitizedPath = endpointPath.StartsWith('/')
            ? endpointPath
            : "/" + endpointPath;

        return sanitizedBaseUrl + sanitizedPath;
    }

    private static string? ResolveValue(string? preferredValue, string? fallbackValue)
    {
        if (!string.IsNullOrWhiteSpace(preferredValue))
        {
            return preferredValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallbackValue))
        {
            return fallbackValue.Trim();
        }

        return null;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static int? GetInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(element.GetString(), out var value) => value,
            _ => null
        };
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

public sealed record EsmsSendRequest(
    string Phone,
    string Content,
    string? RequestId = null,
    string? Brandname = null,
    string? SmsType = null,
    string? IsUnicode = null,
    string? Sandbox = null,
    string? CampaignId = null,
    string? CallbackUrl = null);

public sealed record EsmsSendResult(
    string RequestId,
    string? CodeResult,
    string? SmsId,
    string? ErrorMessage,
    int? CountRegenerate,
    string RawResponse)
{
    public bool IsAccepted => string.Equals(CodeResult, "100", StringComparison.Ordinal);
}
