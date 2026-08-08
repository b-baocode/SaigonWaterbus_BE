using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Assistant;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Ai;

/// <summary>
/// Cài đặt <see cref="ISpeechToTextService"/> bằng Google Cloud Speech-to-Text **v2**
/// (`recognizers/_:recognize` với config inline).
///
/// DÙNG v2 CHỨ KHÔNG v1 — đo thật ngày 2026-08-05 trên cùng clip 5.3s tiếng Việt, cùng máy:
///
///   Gemini flash-lite         trung vị 4.39s   chép chuẩn, có dấu câu
///   Cloud v1 latest_short     trung vị 4.20s   sai tên riêng, không dấu câu
///   Cloud v2 long             trung vị 2.46s   sai tên riêng, không dấu câu  ← chọn cái này
///   Cloud v2 chirp_2          trung vị 6.50s   chép chuẩn nhất nhưng chậm nhất
///
/// v2 nhanh gần gấp đôi v1 nên bù lại được phần nào chỗ đau nhất của tính năng (độ trễ).
/// Phần "sai tên riêng" chữa bằng speech adaptation — xem <see cref="BuildAdaptation"/>.
///
/// CHÚ Ý ENDPOINT: `asia-southeast1` (Singapore, gần Việt Nam nhất) **không hỗ trợ vi-VN** cho
/// model short/long — đã thử, trả 400. Nên vẫn phải đi qua `global`.
/// </summary>
public sealed class GoogleCloudSpeechToTextService : ISpeechToTextService
{
    public const string HttpClientName = "GoogleCloudSpeechToText";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GoogleCloudCredentials _credentials;
    private readonly GoogleCloudSpeechToTextOptions _options;

    public GoogleCloudSpeechToTextService(
        IHttpClientFactory httpClientFactory,
        GoogleCloudCredentials credentials,
        IOptions<GoogleCloudSpeechToTextOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _credentials = credentials;
        _options = options.Value;
    }

    public async Task<string> TranscribeAsync(
        SpeechRecognitionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Audio.Length == 0)
        {
            return string.Empty;
        }

        var projectId = ResolveProjectId();

        var config = new JsonObject
        {
            // autoDecodingConfig: Google tự đọc header để biết định dạng và sample rate.
            // Nhờ nó mà không phải map encoding thủ công như v1 — wav/mp3/ogg/webm đều được.
            // (mp4/aac của iOS Safari VẪN không được — đó là lý do FE phải gửi WAV.)
            ["autoDecodingConfig"] = new JsonObject(),
            ["languageCodes"] = new JsonArray { LanguageCodeOf(request.Language) },
            ["model"] = _options.Model,
            ["features"] = new JsonObject { ["enableAutomaticPunctuation"] = true },
        };

        // KHÔNG gửi `adaptation` (gợi ý tên riêng) — ĐÃ THỬ và bị từ chối:
        //   400 INVALID_ARGUMENT "Config contains unsupported fields"
        // Model `long` ở location `global` không nhận speech adaptation. Vì vậy tên riêng vẫn
        // bị chép sai ("cầu Ba Son" -> "cậu Ba Son"); phần chữa nằm ở system prompt của trợ lý,
        // vốn đã dặn model rằng câu khách nói là do máy chép nên có thể sai chính tả.
        //
        // BẪY GỠ LỖI: gửi field không hợp lệ kèm body lớn (audio base64 vài trăm KB) thì Google
        // đóng kết nối giữa lúc mình còn đang ghi, và .NET báo thành
        // "An existing connection was forcibly closed" — nuốt mất thông báo 400 thật. Gặp lỗi
        // socket kiểu này thì thử lại request với body nhỏ để đọc được lỗi thật.

        var body = new JsonObject
        {
            ["config"] = config,
            ["content"] = Convert.ToBase64String(request.Audio),
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = $"{_options.ApiBaseUrl.TrimEnd('/')}/projects/{projectId}"
                + $"/locations/{_options.Location}/recognizers/_:recognize";

        using var response = await GoogleApiCall.PostAsync(
            client,
            _credentials,
            url,
            _options.ApiKey,
            $"{GoogleCloudSpeechToTextOptions.SectionName}:ApiKey",
            body,
            cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Cloud STT lỗi {(int)response.StatusCode}: {json}");
        }

        return ParseTranscript(json);
    }

    /// <summary>
    /// v2 bắt buộc có project id trong URL. Lấy từ chính file service account để khỏi phải khai
    /// hai chỗ rồi lệch nhau; khai tay trong options thì ưu tiên cái khai tay.
    /// </summary>
    private string ResolveProjectId()
    {
        if (!string.IsNullOrWhiteSpace(_options.ProjectId))
        {
            return _options.ProjectId.Trim();
        }

        return _credentials.ProjectId
            ?? throw new InvalidOperationException(
                "Không xác định được project id cho Cloud STT v2. Đặt "
                + $"'{GoogleCloudSpeechToTextOptions.SectionName}:ProjectId', hoặc dùng service "
                + "account (project id nằm sẵn trong file JSON).");
    }

    private string LanguageCodeOf(string? language) =>
        AssistantLanguage.Resolve(language) == AssistantLanguage.English
            ? _options.EnglishLanguageCode
            : _options.VietnameseLanguageCode;

    /// <summary>
    /// Cloud STT cắt audio thành nhiều `results`, mỗi cái có danh sách `alternatives` xếp theo
    /// độ tin cậy. Ghép alternative đầu của từng đoạn lại thành câu hoàn chỉnh.
    /// </summary>
    private static string ParseTranscript(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("results", out var results))
        {
            return string.Empty;  // Không nghe ra tiếng nói nào — Google bỏ hẳn field results.
        }

        var parts = new List<string>();
        foreach (var result in results.EnumerateArray())
        {
            if (result.TryGetProperty("alternatives", out var alternatives)
                && alternatives.GetArrayLength() > 0
                && alternatives[0].TryGetProperty("transcript", out var transcript))
            {
                var text = transcript.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text.Trim());
                }
            }
        }

        return string.Join(" ", parts);
    }
}
