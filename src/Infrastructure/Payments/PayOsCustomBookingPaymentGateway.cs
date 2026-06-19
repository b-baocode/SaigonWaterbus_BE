using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Payments;

public sealed class PayOsCustomBookingPaymentGateway : ICustomBookingPaymentGateway
{
    private const string HttpClientName = "PayOS";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<PayOsOptions> _optionsMonitor;
    private readonly ILogger<PayOsCustomBookingPaymentGateway> _logger;

    public PayOsCustomBookingPaymentGateway(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<PayOsOptions> optionsMonitor,
        ILogger<PayOsCustomBookingPaymentGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task<CustomBookingDepositPaymentResult> CreateDepositPaymentAsync(
        CustomBookingDepositPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var options = GetEnabledOptions();
        var signature = PayOsSignature.CreatePaymentRequestSignature(
            request.Amount,
            options.CancelUrl,
            request.Description,
            request.OrderCode,
            options.ReturnUrl,
            options.ChecksumKey);
        var payload = new PayOsCreatePaymentRequest(
            request.OrderCode,
            request.Amount,
            request.Description,
            request.BuyerName,
            request.BuyerEmail,
            request.BuyerPhone,
            [new PayOsPaymentItem(request.ItemName, 1, request.Amount)],
            options.CancelUrl,
            options.ReturnUrl,
            request.ExpiredAt?.ToUnixTimeSeconds(),
            signature);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{options.ApiBaseUrl.TrimEnd('/')}/v2/payment-requests");
            httpRequest.Headers.TryAddWithoutValidation("x-client-id", options.ClientId);
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);
            if (!string.IsNullOrWhiteSpace(options.PartnerCode))
            {
                httpRequest.Headers.TryAddWithoutValidation("x-partner-code", options.PartnerCode);
            }

            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Content = JsonContent.Create(payload);

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "PayOS create payment failed. Status: {StatusCode}, Body: {Body}",
                    (int)response.StatusCode,
                    Truncate(body, 500));
                throw new PaymentGatewayException("Không tạo được link thanh toán PayOS.");
            }

            var result = await response.Content.ReadFromJsonAsync<PayOsApiResponse<PayOsCreatePaymentData>>(
                cancellationToken);
            if (result?.Code != "00" || result.Data is null)
            {
                _logger.LogWarning(
                    "PayOS create payment returned business error. Code: {Code}, Desc: {Desc}",
                    result?.Code,
                    result?.Desc);
                throw new PaymentGatewayException(result?.Desc ?? "Không tạo được link thanh toán PayOS.");
            }

            return new CustomBookingDepositPaymentResult(
                result.Data.PaymentLinkId,
                result.Data.CheckoutUrl,
                result.Data.QrCode,
                result.Data.Status);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PaymentGatewayException("Không tạo được link thanh toán PayOS.", ex);
        }
    }

    public async Task<CustomBookingRefundPayoutResult> CreateRefundPayoutAsync(
        CustomBookingRefundPayoutRequest request,
        CancellationToken cancellationToken)
    {
        var options = GetEnabledOptions();
        var payoutCredentials = GetPayoutCredentials(options);
        var category = new[] { "refund", "custom-booking" };
        var signature = PayOsSignature.CreatePayoutRequestSignature(
            request.ReferenceId,
            request.Amount,
            request.Description,
            request.ToBin,
            request.ToAccountNumber,
            category,
            payoutCredentials.ChecksumKey);

        var payload = new PayOsCreatePayoutRequest(
            request.ReferenceId,
            request.Amount,
            request.Description,
            request.ToBin,
            request.ToAccountNumber,
            category);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{options.ApiBaseUrl.TrimEnd('/')}/v1/payouts");
            httpRequest.Headers.TryAddWithoutValidation("x-client-id", payoutCredentials.ClientId);
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", payoutCredentials.ApiKey);
            httpRequest.Headers.TryAddWithoutValidation("x-idempotency-key", request.IdempotencyKey);
            httpRequest.Headers.TryAddWithoutValidation("x-signature", signature);
            if (!string.IsNullOrWhiteSpace(payoutCredentials.PartnerCode))
            {
                httpRequest.Headers.TryAddWithoutValidation("x-partner-code", payoutCredentials.PartnerCode);
            }

            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Content = JsonContent.Create(payload);

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "PayOS refund payout failed. ReferenceId: {ReferenceId}, Status: {StatusCode}, Body: {Body}",
                    request.ReferenceId,
                    (int)response.StatusCode,
                    Truncate(body, 500));
                throw new PaymentGatewayException(CreatePayOsErrorMessage(
                    "Không tạo được lệnh hoàn tiền PayOS",
                    response.StatusCode,
                    body));
            }

            var result = JsonSerializer.Deserialize<PayOsApiResponse<PayOsCreatePayoutData>>(body);
            if (result?.Code != "00" || result.Data is null)
            {
                _logger.LogWarning(
                    "PayOS refund payout returned business error. ReferenceId: {ReferenceId}, Code: {Code}, Desc: {Desc}",
                    request.ReferenceId,
                    result?.Code,
                    result?.Desc);
                throw new PaymentGatewayException(result?.Desc ?? "Không tạo được lệnh hoàn tiền PayOS.");
            }

            return ToRefundPayoutResult(result.Data, result.Desc);
        }
        catch (PaymentGatewayException)
        {
            var reconciled = await TryGetRefundPayoutByReferenceIdAsync(
                options,
                payoutCredentials,
                request.ReferenceId,
                cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw;
        }
        catch (Exception ex)
        {
            var reconciled = await TryGetRefundPayoutByReferenceIdAsync(
                options,
                payoutCredentials,
                request.ReferenceId,
                cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new PaymentGatewayException("Không tạo được lệnh hoàn tiền PayOS.", ex);
        }
    }

    public async Task<CustomBookingRefundPayoutResult?> GetRefundPayoutByReferenceIdAsync(
        string referenceId,
        CancellationToken cancellationToken)
    {
        var options = GetEnabledOptions();
        var payoutCredentials = GetPayoutCredentials(options);
        return await GetRefundPayoutByReferenceIdCoreAsync(
            options,
            payoutCredentials,
            referenceId,
            cancellationToken);
    }

    public async Task<CustomBookingPaymentCancellationResult> CancelPaymentAsync(
        long orderCode,
        string reason,
        CancellationToken cancellationToken)
    {
        var options = GetEnabledOptions();
        var payload = new PayOsCancelPaymentRequest(reason);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{options.ApiBaseUrl.TrimEnd('/')}/v2/payment-requests/{orderCode}/cancel");
            httpRequest.Headers.TryAddWithoutValidation("x-client-id", options.ClientId);
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);
            if (!string.IsNullOrWhiteSpace(options.PartnerCode))
            {
                httpRequest.Headers.TryAddWithoutValidation("x-partner-code", options.PartnerCode);
            }

            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Content = JsonContent.Create(payload);

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "PayOS cancel payment failed. OrderCode: {OrderCode}, Status: {StatusCode}, Body: {Body}",
                    orderCode,
                    (int)response.StatusCode,
                    Truncate(body, 500));
                throw new PaymentGatewayException("Không hủy được link thanh toán PayOS.");
            }

            var result = await response.Content.ReadFromJsonAsync<PayOsApiResponse<PayOsPaymentRequestData>>(
                cancellationToken);
            if (result?.Code != "00" || result.Data is null)
            {
                _logger.LogWarning(
                    "PayOS cancel payment returned business error. OrderCode: {OrderCode}, Code: {Code}, Desc: {Desc}",
                    orderCode,
                    result?.Code,
                    result?.Desc);
                throw new PaymentGatewayException(result?.Desc ?? "Không hủy được link thanh toán PayOS.");
            }

            return new CustomBookingPaymentCancellationResult(
                result.Data.Id ?? result.Data.PaymentLinkId,
                result.Data.Status,
                result.Desc);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PaymentGatewayException("Không hủy được link thanh toán PayOS.", ex);
        }
    }

    public async Task<CustomBookingPaymentStatusResult> GetPaymentAsync(
        long orderCode,
        CancellationToken cancellationToken)
    {
        var options = GetEnabledOptions();

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{options.ApiBaseUrl.TrimEnd('/')}/v2/payment-requests/{orderCode}");
            httpRequest.Headers.TryAddWithoutValidation("x-client-id", options.ClientId);
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);
            if (!string.IsNullOrWhiteSpace(options.PartnerCode))
            {
                httpRequest.Headers.TryAddWithoutValidation("x-partner-code", options.PartnerCode);
            }

            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "PayOS get payment failed. OrderCode: {OrderCode}, Status: {StatusCode}, Body: {Body}",
                    orderCode,
                    (int)response.StatusCode,
                    Truncate(body, 500));
                throw new PaymentGatewayException("Không kiểm tra được trạng thái thanh toán PayOS.");
            }

            var result = await response.Content.ReadFromJsonAsync<PayOsApiResponse<PayOsPaymentRequestData>>(
                cancellationToken);
            if (result?.Code != "00" || result.Data is null)
            {
                _logger.LogWarning(
                    "PayOS get payment returned business error. OrderCode: {OrderCode}, Code: {Code}, Desc: {Desc}",
                    orderCode,
                    result?.Code,
                    result?.Desc);
                throw new PaymentGatewayException(result?.Desc ?? "Không kiểm tra được trạng thái thanh toán PayOS.");
            }

            return new CustomBookingPaymentStatusResult(
                result.Data.OrderCode ?? orderCode,
                result.Data.Amount,
                result.Data.Status,
                result.Data.Id ?? result.Data.PaymentLinkId,
                result.Data.CheckoutUrl);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PaymentGatewayException("Không kiểm tra được trạng thái thanh toán PayOS.", ex);
        }
    }

    public bool IsValidWebhook(CustomBookingDepositPaymentWebhook webhook)
    {
        var options = _optionsMonitor.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ChecksumKey))
        {
            return false;
        }

        var data = new Dictionary<string, object?>
        {
            ["accountNumber"] = webhook.Data.AccountNumber,
            ["amount"] = webhook.Data.Amount,
            ["code"] = webhook.Data.Code,
            ["counterAccountBankId"] = webhook.Data.CounterAccountBankId,
            ["counterAccountBankName"] = webhook.Data.CounterAccountBankName,
            ["counterAccountName"] = webhook.Data.CounterAccountName,
            ["counterAccountNumber"] = webhook.Data.CounterAccountNumber,
            ["currency"] = webhook.Data.Currency,
            ["desc"] = webhook.Data.Desc,
            ["description"] = webhook.Data.Description,
            ["orderCode"] = webhook.Data.OrderCode,
            ["paymentLinkId"] = webhook.Data.PaymentLinkId,
            ["reference"] = webhook.Data.Reference,
            ["transactionDateTime"] = webhook.Data.TransactionDateTime,
            ["virtualAccountName"] = webhook.Data.VirtualAccountName,
            ["virtualAccountNumber"] = webhook.Data.VirtualAccountNumber
        };
        var expectedSignature = PayOsSignature.CreateSignatureFromData(data, options.ChecksumKey);
        return string.Equals(expectedSignature, webhook.Signature, StringComparison.OrdinalIgnoreCase);
    }

    private PayOsOptions GetEnabledOptions()
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            throw new PaymentGatewayException("PayOS chưa được bật cấu hình.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl)
            || string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ApiKey)
            || string.IsNullOrWhiteSpace(options.ChecksumKey)
            || string.IsNullOrWhiteSpace(options.ReturnUrl)
            || string.IsNullOrWhiteSpace(options.CancelUrl))
        {
            throw new PaymentGatewayException("PayOS chưa cấu hình đủ ClientId, ApiKey, ChecksumKey, ReturnUrl và CancelUrl.");
        }

        return options;
    }

    private static PayOsCredentials GetPayoutCredentials(PayOsOptions options)
    {
        var clientId = string.IsNullOrWhiteSpace(options.PayoutClientId)
            ? options.ClientId
            : options.PayoutClientId;
        var apiKey = string.IsNullOrWhiteSpace(options.PayoutApiKey)
            ? options.ApiKey
            : options.PayoutApiKey;
        var checksumKey = string.IsNullOrWhiteSpace(options.PayoutChecksumKey)
            ? options.ChecksumKey
            : options.PayoutChecksumKey;
        var partnerCode = string.IsNullOrWhiteSpace(options.PayoutPartnerCode)
            ? options.PartnerCode
            : options.PayoutPartnerCode;

        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(checksumKey))
        {
            throw new PaymentGatewayException("PayOS chưa cấu hình đủ PayoutClientId, PayoutApiKey và PayoutChecksumKey để hoàn tiền.");
        }

        return new PayOsCredentials(clientId, apiKey, checksumKey, partnerCode);
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];

    private async Task<CustomBookingRefundPayoutResult?> TryGetRefundPayoutByReferenceIdAsync(
        PayOsOptions options,
        PayOsCredentials payoutCredentials,
        string referenceId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetRefundPayoutByReferenceIdCoreAsync(
                options,
                payoutCredentials,
                referenceId,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "PayOS refund payout reconciliation failed after create failure. ReferenceId: {ReferenceId}",
                referenceId);
            return null;
        }
    }

    private async Task<CustomBookingRefundPayoutResult?> GetRefundPayoutByReferenceIdCoreAsync(
        PayOsOptions options,
        PayOsCredentials payoutCredentials,
        string referenceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var query = $"limit=1&offset=0&referenceId={Uri.EscapeDataString(referenceId)}";
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{options.ApiBaseUrl.TrimEnd('/')}/v1/payouts?{query}");
            httpRequest.Headers.TryAddWithoutValidation("x-client-id", payoutCredentials.ClientId);
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", payoutCredentials.ApiKey);
            if (!string.IsNullOrWhiteSpace(payoutCredentials.PartnerCode))
            {
                httpRequest.Headers.TryAddWithoutValidation("x-partner-code", payoutCredentials.PartnerCode);
            }

            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "PayOS get refund payout failed. ReferenceId: {ReferenceId}, Status: {StatusCode}, Body: {Body}",
                    referenceId,
                    (int)response.StatusCode,
                    Truncate(body, 500));
                throw new PaymentGatewayException(CreatePayOsErrorMessage(
                    "Không kiểm tra được trạng thái hoàn tiền PayOS",
                    response.StatusCode,
                    body));
            }

            var result = JsonSerializer.Deserialize<PayOsApiResponse<PayOsPayoutListData>>(body);
            if (result?.Code != "00" || result.Data is null)
            {
                _logger.LogWarning(
                    "PayOS get refund payout returned business error. ReferenceId: {ReferenceId}, Code: {Code}, Desc: {Desc}",
                    referenceId,
                    result?.Code,
                    result?.Desc);
                throw new PaymentGatewayException(result?.Desc ?? "Không kiểm tra được trạng thái hoàn tiền PayOS.");
            }

            var payout = result.Data.Payouts?.Values.FirstOrDefault(x =>
                string.Equals(x.ReferenceId, referenceId, StringComparison.OrdinalIgnoreCase))
                ?? result.Data.Payouts?.Values.FirstOrDefault();
            return payout is null
                ? null
                : ToRefundPayoutResult(payout, result.Desc);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PaymentGatewayException("Không kiểm tra được trạng thái hoàn tiền PayOS.", ex);
        }
    }

    private static CustomBookingRefundPayoutResult ToRefundPayoutResult(
        PayOsCreatePayoutData data,
        string? description)
    {
        var transaction = data.Transactions?.Values.FirstOrDefault();
        return new CustomBookingRefundPayoutResult(
            data.Id,
            data.ApprovalState
                ?? transaction?.State
                ?? "Created",
            description,
            data.ReferenceId ?? transaction?.ReferenceId,
            transaction?.Amount);
    }

    private static string CreatePayOsErrorMessage(
        string fallbackMessage,
        System.Net.HttpStatusCode statusCode,
        string body)
    {
        var description = TryReadPayOsDescription(body);
        if (!string.IsNullOrWhiteSpace(description))
        {
            return $"{fallbackMessage}: {description}";
        }

        return string.IsNullOrWhiteSpace(body)
            ? $"{fallbackMessage}. HTTP {(int)statusCode}."
            : $"{fallbackMessage}. HTTP {(int)statusCode}: {Truncate(body, 300)}";
    }

    private static string? TryReadPayOsDescription(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<PayOsApiResponse<object>>(body);
            return string.IsNullOrWhiteSpace(result?.Desc)
                ? null
                : result.Desc;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record PayOsCredentials(
        string ClientId,
        string ApiKey,
        string ChecksumKey,
        string? PartnerCode);

    private sealed record PayOsCreatePaymentRequest(
        [property: JsonPropertyName("orderCode")]
        long OrderCode,
        [property: JsonPropertyName("amount")]
        long Amount,
        [property: JsonPropertyName("description")]
        string Description,
        [property: JsonPropertyName("buyerName")]
        string BuyerName,
        [property: JsonPropertyName("buyerEmail")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? BuyerEmail,
        [property: JsonPropertyName("buyerPhone")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? BuyerPhone,
        [property: JsonPropertyName("items")]
        IReadOnlyCollection<PayOsPaymentItem> Items,
        [property: JsonPropertyName("cancelUrl")]
        string CancelUrl,
        [property: JsonPropertyName("returnUrl")]
        string ReturnUrl,
        [property: JsonPropertyName("expiredAt")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        long? ExpiredAt,
        [property: JsonPropertyName("signature")]
        string Signature);

    private sealed record PayOsPaymentItem(
        [property: JsonPropertyName("name")]
        string Name,
        [property: JsonPropertyName("quantity")]
        int Quantity,
        [property: JsonPropertyName("price")]
        long Price);

    private sealed record PayOsCancelPaymentRequest(
        [property: JsonPropertyName("cancellationReason")]
        string CancellationReason);

    private sealed record PayOsCreatePayoutRequest(
        [property: JsonPropertyName("referenceId")]
        string ReferenceId,
        [property: JsonPropertyName("amount")]
        long Amount,
        [property: JsonPropertyName("description")]
        string Description,
        [property: JsonPropertyName("toBin")]
        string ToBin,
        [property: JsonPropertyName("toAccountNumber")]
        string ToAccountNumber,
        [property: JsonPropertyName("category")]
        IReadOnlyCollection<string> Category);

    private sealed record PayOsApiResponse<T>(
        [property: JsonPropertyName("code")]
        string Code,
        [property: JsonPropertyName("desc")]
        string Desc,
        [property: JsonPropertyName("data")]
        T? Data,
        [property: JsonPropertyName("signature")]
        string? Signature);

    private sealed record PayOsCreatePaymentData(
        [property: JsonPropertyName("paymentLinkId")]
        string PaymentLinkId,
        [property: JsonPropertyName("checkoutUrl")]
        string CheckoutUrl,
        [property: JsonPropertyName("qrCode")]
        string? QrCode,
        [property: JsonPropertyName("status")]
        string Status);

    private sealed record PayOsPaymentRequestData(
        [property: JsonPropertyName("id")]
        string? Id,
        [property: JsonPropertyName("orderCode")]
        long? OrderCode,
        [property: JsonPropertyName("amount")]
        long? Amount,
        [property: JsonPropertyName("paymentLinkId")]
        string? PaymentLinkId,
        [property: JsonPropertyName("checkoutUrl")]
        string? CheckoutUrl,
        [property: JsonPropertyName("status")]
        string Status);

    private sealed record PayOsCreatePayoutData(
        [property: JsonPropertyName("id")]
        string? Id,
        [property: JsonPropertyName("referenceId")]
        string? ReferenceId,
        [property: JsonPropertyName("approvalState")]
        string? ApprovalState,
        [property: JsonPropertyName("transactions")]
        IReadOnlyDictionary<string, PayOsPayoutTransaction>? Transactions);

    private sealed record PayOsPayoutListData(
        [property: JsonPropertyName("payouts")]
        IReadOnlyDictionary<string, PayOsCreatePayoutData>? Payouts);

    private sealed record PayOsPayoutTransaction(
        [property: JsonPropertyName("referenceId")]
        string? ReferenceId,
        [property: JsonPropertyName("amount")]
        long? Amount,
        [property: JsonPropertyName("state")]
        string? State);
}
