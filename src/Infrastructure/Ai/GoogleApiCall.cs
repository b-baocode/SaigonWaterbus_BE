using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SaigonWaterbus.Application.Common.Exceptions;

namespace SaigonWaterbus.Infrastructure.Ai;

/// <summary>
/// Gọi một REST API của Google Cloud, tự chọn kiểu xác thực. Tách ra vì Text-to-Speech và
/// Speech-to-Text cần y hệt nhau — hai bản copy sẽ lệch nhau lúc sửa.
/// </summary>
internal static class GoogleApiCall
{
    /// <summary>
    /// Ưu tiên service account (Bearer token); không có thì lùi về API key trên query string.
    /// Không có cả hai thì ném lỗi kèm hướng dẫn cấu hình.
    /// </summary>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        GoogleCloudCredentials credentials,
        string endpointUrl,
        string apiKey,
        string apiKeySettingName,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        var useServiceAccount = credentials.IsConfigured;

        if (!useServiceAccount && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Chưa cấu hình xác thực Google Cloud. Chọn MỘT trong hai: đặt "
                + $"'{GoogleCloudCredentialsOptions.SectionName}:CredentialsPath' trỏ tới file JSON "
                + $"service account, hoặc đặt '{apiKeySettingName}' bằng API key dạng chuỗi.");
        }

        var url = useServiceAccount
            ? endpointUrl
            : $"{endpointUrl}{(endpointUrl.Contains('?') ? '&' : '?')}key={apiKey}";

        // Tuần tự hoá ra chuỗi TRƯỚC rồi mới bọc vào StringContent, thay vì JsonContent.Create.
        // Lý do: JsonContent ghi thẳng ra stream nên HttpClient không biết độ dài và chuyển sang
        // `Transfer-Encoding: chunked`. Với body lớn (audio base64 cỡ vài trăm KB) thì Google
        // ngắt kết nối giữa chừng — đã gặp thật: "An existing connection was forcibly closed".
        // StringContent đặt sẵn Content-Length nên đi trọn.
        var payload = body.ToJsonString();

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        if (useServiceAccount)
        {
            var token = await credentials.GetAccessTokenAsync(cancellationToken);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(message, cancellationToken);
    }

    /// <summary>
    /// Dựng ngoại lệ đúng loại từ mã lỗi HTTP, để tầng Web trả đúng mã cho client.
    ///
    /// 4xx = request của mình sai (tên giọng không tồn tại, thiếu tham số, định dạng lạ) → lỗi
    /// người gọi sửa được, phải nói rõ lý do. 5xx / quá tải / mạng → provider hỏng, người gọi
    /// không làm gì được ngoài thử lại.
    /// </summary>
    public static Exception ToException(string providerName, HttpStatusCode statusCode, string body)
    {
        var reason = ExtractGoogleMessage(body) ?? body;
        var code = (int)statusCode;

        // 429 tuy là 4xx nhưng người gọi không sửa được gì — xếp cùng nhóm "thử lại sau".
        if (code is >= 400 and < 500 and not 429)
        {
            return new SpeechRequestException($"{providerName}: {reason}");
        }

        return new InvalidOperationException($"{providerName} lỗi {code}: {body}");
    }

    /// <summary>Rút câu `error.message` của Google; không parse được thì trả null để dùng nguyên body.</summary>
    private static string? ExtractGoogleMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error)
                   && error.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
