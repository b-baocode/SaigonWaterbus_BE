using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Payments;

public sealed class VietQrBankAccountLookupService : IBankAccountLookupService
{
    public const string HttpClientName = "VietQR";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<BankAccountLookupOptions> _optionsMonitor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VietQrBankAccountLookupService> _logger;

    public VietQrBankAccountLookupService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<BankAccountLookupOptions> optionsMonitor,
        TimeProvider timeProvider,
        ILogger<VietQrBankAccountLookupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = optionsMonitor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<BankAccountLookupServiceResult> LookupAsync(
        BankAccountLookupServiceRequest request,
        CancellationToken cancellationToken)
    {
        var options = GetEnabledOptions();
        var bankBin = request.BankBin.Trim();
        var accountNumber = request.AccountNumber.Trim();
        var payload = new VietQrLookupRequest(long.Parse(bankBin), accountNumber);

        try
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{options.ApiBaseUrl.TrimEnd('/')}/v2/lookup");
            httpRequest.Headers.TryAddWithoutValidation("x-client-id", options.ClientId);
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Content = JsonContent.Create(payload, options: JsonOptions);

            using var response = await _httpClientFactory
                .CreateClient(HttpClientName)
                .SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "VietQR bank account lookup failed. BankBin: {BankBin}, AccountNumber: {AccountNumber}, Status: {StatusCode}, Body: {Body}",
                    bankBin,
                    MaskAccountNumber(accountNumber),
                    (int)response.StatusCode,
                    Truncate(body, 500));

                throw new PaymentGatewayException(CreateLookupErrorMessage(
                    "Không tra cứu được tên chủ tài khoản",
                    response.StatusCode,
                    body));
            }

            var result = JsonSerializer.Deserialize<VietQrLookupResponse>(body, JsonOptions);
            var accountName = result?.Data?.AccountName?.Trim();
            if (result?.Code != "00" || string.IsNullOrWhiteSpace(accountName))
            {
                throw new PaymentGatewayException(result?.Desc ?? "Không tra cứu được tên chủ tài khoản.");
            }

            return new BankAccountLookupServiceResult(
                bankBin,
                accountNumber,
                accountName,
                ResolveProvider(options),
                _timeProvider.GetUtcNow(),
                result.Desc);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PaymentGatewayException(
                "Gateway tra cứu tài khoản ngân hàng tạm không phản hồi. Vui lòng thử lại sau hoặc nhập tên chủ tài khoản thủ công.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException(
                "Gateway tra cứu tài khoản ngân hàng tạm không phản hồi. Vui lòng thử lại sau hoặc nhập tên chủ tài khoản thủ công.",
                ex);
        }
        catch (Exception ex)
        {
            throw new PaymentGatewayException("Không tra cứu được tên chủ tài khoản.", ex);
        }
    }

    private BankAccountLookupOptions GetEnabledOptions()
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            throw new PaymentGatewayException("Chưa bật cấu hình tra cứu tài khoản ngân hàng.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl)
            || string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new PaymentGatewayException("Chưa cấu hình đủ BankAccountLookup ApiBaseUrl, ClientId và ApiKey.");
        }

        if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out _))
        {
            throw new PaymentGatewayException("BankAccountLookup ApiBaseUrl không hợp lệ.");
        }

        return options;
    }

    private static string ResolveProvider(BankAccountLookupOptions options) =>
        string.IsNullOrWhiteSpace(options.Provider) ? "VietQR" : options.Provider.Trim();

    private static string CreateLookupErrorMessage(
        string fallbackMessage,
        HttpStatusCode statusCode,
        string body)
    {
        if (statusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable)
        {
            return "Gateway tra cứu tài khoản ngân hàng tạm không phản hồi. Vui lòng thử lại sau hoặc nhập tên chủ tài khoản thủ công.";
        }

        var description = TryReadVietQrDescription(body);
        if (!string.IsNullOrWhiteSpace(description))
        {
            return $"{fallbackMessage}: {description}";
        }

        return string.IsNullOrWhiteSpace(body)
            ? $"{fallbackMessage}. HTTP {(int)statusCode}."
            : $"{fallbackMessage}. HTTP {(int)statusCode}: {Truncate(body, 300)}";
    }

    private static string? TryReadVietQrDescription(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<VietQrLookupResponse>(body, JsonOptions);
            return string.IsNullOrWhiteSpace(result?.Desc) ? null : result.Desc;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string MaskAccountNumber(string accountNumber) =>
        accountNumber.Length <= 4 ? "****" : new string('*', accountNumber.Length - 4) + accountNumber[^4..];

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];

    private sealed record VietQrLookupRequest(
        [property: JsonPropertyName("bin")]
        long Bin,
        [property: JsonPropertyName("accountNumber")]
        string AccountNumber);

    private sealed record VietQrLookupResponse(
        [property: JsonPropertyName("code")]
        string? Code,
        [property: JsonPropertyName("desc")]
        string? Desc,
        [property: JsonPropertyName("data")]
        VietQrLookupData? Data);

    private sealed record VietQrLookupData(
        [property: JsonPropertyName("accountName")]
        string? AccountName);
}
