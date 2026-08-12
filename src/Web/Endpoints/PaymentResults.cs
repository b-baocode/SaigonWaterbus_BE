using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Infrastructure.Payments;
using SaigonWaterbus.Application.Payments;

namespace SaigonWaterbus.Web.Endpoints;

/// <summary>
/// Endpoint phục vụ return URL sau khi PayOS thanh toán xong.
/// Mục tiêu: handle 4 case của mobile app:
///   1. Trình duyệt thường → hiện trang web kết quả
///   2. App đã cài → Universal Link tự mở app (iOS/Android)
///   3. App chưa cài → fallback về trang web
///   4. App mở lại sau khi user rời PayOS → app gọi /api/payments/order-code/{orderCode}/sync
/// </summary>
public sealed class PaymentResults : IEndpointGroup
{
    public static string RoutePrefix => "/payment";

    public static void Map(RouteGroupBuilder group)
    {
        // Return URL mà PayOS redirect về sau khi thanh toán.
        // VD: https://waterbus.top/payment/success?orderCode=123&status=PAID&code=00
        group.MapGet(Success, "success")
            .AllowAnonymous()
            .WithSummary("PayOS return URL - smart redirect app/web")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Public (PayOS redirect)",
                null,
                "PayOS redirect user về URL này sau khi thanh toán xong (cả thanh công/thất bại/hủy).",
                "Page sẽ thử mở app qua Universal Link trên iOS/Android.",
                "Nếu app không cài hoặc không mở, fallback hiển thị kết quả trên web.",
                "FE/mobile có thể gọi thêm POST /api/payments/order-code/{orderCode}/sync để lấy status mới nhất."));

        // PayOS may send the user here after they cancel the checkout flow.
        // Keep it on the same app/web return path instead of letting the
        // configured CancelUrl resolve to a 404 page.
        group.MapGet(Cancel, "cancel")
            .AllowAnonymous()
            .WithSummary("PayOS cancel URL - smart redirect app/web");
    }

    private static async Task<IResult> Cancel(
        [FromQuery] long? orderCode,
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "code")] string? code,
        [FromServices] IOptions<PayOsOptions> payOsOptions,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        if (orderCode.HasValue)
        {
            await sender.Send(new CancelPaymentByOrderCodeCommand(orderCode.Value), ct);
        }

        return Success(orderCode, string.IsNullOrWhiteSpace(status) ? "CANCELLED" : status, code, payOsOptions);
    }

    private static IResult Success(
        [FromQuery] long? orderCode,
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "code")] string? code,
        [FromServices] IOptions<PayOsOptions> payOsOptions)
    {
        var universalLinkBase = !string.IsNullOrWhiteSpace(payOsOptions.Value.ReturnUniversalLinkBase)
            ? payOsOptions.Value.ReturnUniversalLinkBase
            : ExtractBaseFromReturnUrl(payOsOptions.Value.ReturnUrl);

        // Build query string để pass sang app/web giống nhau
        var queryParams = new List<string>();
        if (orderCode.HasValue)
        {
            queryParams.Add($"orderCode={orderCode.Value}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            queryParams.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            queryParams.Add($"code={Uri.EscapeDataString(code)}");
        }

        var queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : string.Empty;
        var deepLinkUrl = $"{universalLinkBase.TrimEnd('/')}/payment/success{queryString}";
        // Temporary/fallback path when Universal Link cannot be verified yet
        // (for example while the Apple Team ID is still unavailable).
        var customSchemeUrl = $"saigonwaterbus://payment/success{queryString}";

        var html = BuildSmartRedirectHtml(deepLinkUrl, customSchemeUrl, orderCode, status, code);
        return Results.Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// HTML tối thiểu, không cần framework. Logic:
    ///   - Thử mở Universal Link ngay (iOS/Android sẽ switch sang app).
    ///   - Set timeout 1.5s: nếu tab vẫn visible (tức app không mở) → render kết quả trên web.
    /// </summary>
    private static string BuildSmartRedirectHtml(string deepLinkUrl, string customSchemeUrl, long? orderCode, string? status, string? code)
    {
        var orderCodeJs = orderCode.HasValue ? orderCode.Value.ToString() : "null";
        var statusJs = string.IsNullOrWhiteSpace(status) ? "null" : $"\"{EscapeJs(status)}\"";
        var codeJs = string.IsNullOrWhiteSpace(code) ? "null" : $"\"{EscapeJs(code)}\"";

        // Lưu ý: KHÔNG dùng document.hidden (deprecated). Dùng visibilitychange.
        return $$"""
            <!doctype html>
            <html lang="vi">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1" />
              <title>Kết quả thanh toán - Saigon Waterbus</title>
              <meta name="theme-color" content="#0ea5e9" />
              <style>
                body { margin: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                       background: #f8fafc; color: #0f172a; min-height: 100vh; display: flex; align-items: center; justify-content: center; }
                .card { background: white; max-width: 420px; width: calc(100% - 32px); border-radius: 16px;
                        padding: 32px 24px; box-shadow: 0 10px 30px rgba(15, 23, 42, .08); text-align: center; }
                h1 { font-size: 22px; margin: 0 0 8px; }
                p  { margin: 8px 0; color: #475569; font-size: 15px; line-height: 1.5; }
                .status-success { color: #16a34a; }
                .status-failed  { color: #dc2626; }
                .status-cancel  { color: #d97706; }
                .meta { background: #f1f5f9; border-radius: 10px; padding: 12px; margin: 16px 0; font-size: 13px; }
                .btn { display: inline-block; margin-top: 16px; padding: 12px 24px; background: #0ea5e9; color: white;
                       border-radius: 10px; text-decoration: none; font-weight: 600; font-size: 15px; }
                .hint { font-size: 13px; color: #94a3b8; margin-top: 16px; }
                .spinner { width: 32px; height: 32px; border: 3px solid #e2e8f0; border-top-color: #0ea5e9;
                           border-radius: 50%; animation: spin 1s linear infinite; margin: 0 auto 16px; }
                @keyframes spin { to { transform: rotate(360deg); } }
              </style>
            </head>
            <body>
              <div class="card">
                <div id="loading">
                  <div class="spinner"></div>
                  <h1>Đang xử lý thanh toán…</h1>
                  <p>Vui lòng chờ trong giây lát.</p>
                </div>

                <div id="result" hidden>
                  <h1 id="title">Kết quả thanh toán</h1>
                  <p id="message"></p>
                  <div class="meta">
                    <div>Mã đơn PayOS: <strong id="orderCode"></strong></div>
                    <div>Trạng thái: <strong id="status"></strong></div>
                  </div>
                  <a id="openAppBtn" class="btn" href="#">Mở ứng dụng Waterbus</a>
                  <a id="homeBtn" class="btn" href="/">Về trang chủ</a>
                  <p class="hint">Mở app Saigon Waterbus để xem chi tiết booking.</p>
                </div>
              </div>

              <script>
                (function () {
                  var DEEP_LINK = {{System.Text.Json.JsonSerializer.Serialize(deepLinkUrl)}};
                  var CUSTOM_SCHEME_URL = {{System.Text.Json.JsonSerializer.Serialize(customSchemeUrl)}};
                  var ORDER_CODE = {{orderCodeJs}};
                  var STATUS = {{statusJs}};
                  var CODE = {{codeJs}};

                  var ua = navigator.userAgent || '';
                  var isIOS = /iPad|iPhone|iPod/.test(ua) && !window.MSStream;
                  var isAndroid = /Android/.test(ua);

                  // Case 1+2: thử mở app qua Universal Link ngay khi load page.
                  // Universal Link chỉ work khi page được serve từ HTTPS và có apple-app-site-association / assetlinks.json.
                  if (isIOS || isAndroid) {
                    try {
                      window.location.replace(DEEP_LINK);
                    } catch (e) { /* ignore */ }
                  }

                  // Case 3 fallback: nếu sau 1.5s tab vẫn visible → app không mở → hiển thị kết quả trên web.
                  var showed = false;
                  function showResult() {
                    if (showed) return;
                    showed = true;
                    document.getElementById('loading').hidden = true;
                    var r = document.getElementById('result');
                    r.hidden = false;

                    var normalized = (STATUS || '').toUpperCase();
                    var isSuccess = normalized === 'PAID' || CODE === '00';
                    var isCancel = normalized === 'CANCELLED' || normalized === 'CANCELED';
                    var titleEl = document.getElementById('title');
                    var msgEl = document.getElementById('message');
                    var statusEl = document.getElementById('status');

                    if (isSuccess) {
                      titleEl.textContent = 'Thanh toán thành công';
                      titleEl.className = 'status-success';
                      msgEl.textContent = 'Booking của bạn đã được xác nhận. Cảm ơn bạn đã sử dụng dịch vụ.';
                      statusEl.textContent = 'Thành công';
                    } else if (isCancel) {
                      titleEl.textContent = 'Đã hủy thanh toán';
                      titleEl.className = 'status-cancel';
                      msgEl.textContent = 'Bạn đã hủy giao dịch. Booking vẫn được giữ trong thời hạn báo giá.';
                      statusEl.textContent = 'Đã hủy';
                    } else {
                      titleEl.textContent = 'Thanh toán thất bại';
                      titleEl.className = 'status-failed';
                      msgEl.textContent = 'Giao dịch chưa hoàn tất. Vui lòng thử lại hoặc liên hệ hỗ trợ.';
                      statusEl.textContent = 'Thất bại';
                    }

                    if (ORDER_CODE !== null) {
                      document.getElementById('orderCode').textContent = '#' + ORDER_CODE;
                    } else {
                      document.getElementById('orderCode').parentElement.style.display = 'none';
                    }

                    // Custom scheme does not require AASA/Apple Team ID. It is
                    // intentionally a user-tap fallback; iOS may block an
                    // automatic custom-scheme redirect without a gesture.
                    document.getElementById('openAppBtn').href = CUSTOM_SCHEME_URL;
                  }

                  // Nếu tab/app chuyển sang background (app mở được) → KHÔNG hiển thị web.
                  document.addEventListener('visibilitychange', function () {
                    if (document.visibilityState === 'hidden') {
                      showed = true;
                    } else if (document.visibilityState === 'visible') {
                      showResult();
                    }
                  });

                  setTimeout(showResult, 1500);
                })();
              </script>
            </body>
            </html>
            """;
    }

    private static string EscapeJs(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", string.Empty).Replace("\n", string.Empty);

    /// <summary>
    /// Tách scheme + host (không kèm path) từ <paramref name="returnUrl"/>.
    /// VD: "https://waterbus.top/payment/success" → "https://waterbus.top".
    /// Fallback khi user chưa cấu hình <c>ReturnUniversalLinkBase</c>.
    /// </summary>
    private static string ExtractBaseFromReturnUrl(string returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return returnUrl;
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
        {
            return returnUrl;
        }

        return $"{uri.Scheme}://{uri.Authority}";
    }
}
