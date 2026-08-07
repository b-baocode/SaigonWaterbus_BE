using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

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
}
