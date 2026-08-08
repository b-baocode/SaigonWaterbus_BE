using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Assistant;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Ai;

/// <summary>
/// Cài đặt <see cref="ITextToSpeechService"/> bằng Google Cloud Text-to-Speech (REST
/// text:synthesize). Đây là file DUY NHẤT biết định dạng của Google TTS.
/// </summary>
public sealed class GoogleTextToSpeechService : ITextToSpeechService
{
    public const string HttpClientName = "GoogleTextToSpeech";

    private const string Mp3ContentType = "audio/mpeg";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GoogleCloudCredentials _credentials;
    private readonly GoogleTextToSpeechOptions _options;

    public GoogleTextToSpeechService(
        IHttpClientFactory httpClientFactory,
        GoogleCloudCredentials credentials,
        IOptions<GoogleTextToSpeechOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _credentials = credentials;
        _options = options.Value;
    }

    public async Task<SpeechAudio> SynthesizeAsync(
        SpeechSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        var text = Truncate(request.Text);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cần đọc không được rỗng.", nameof(request));
        }

        var voice = ResolveVoice(request.Voice, request.Language);
        var body = new JsonObject
        {
            ["input"] = new JsonObject { ["text"] = text },
            ["voice"] = new JsonObject
            {
                ["languageCode"] = LanguageCodeOf(voice),
                ["name"] = voice,
            },
            ["audioConfig"] = new JsonObject
            {
                ["audioEncoding"] = "MP3",
                ["speakingRate"] = _options.SpeakingRate,
            },
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await GoogleApiCall.PostAsync(
            client,
            _credentials,
            $"{_options.ApiBaseUrl.TrimEnd('/')}/text:synthesize",
            _options.ApiKey,
            $"{GoogleTextToSpeechOptions.SectionName}:ApiKey",
            body,
            cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // 4xx = tham số mình gửi sai (tên giọng không có thật, ngôn ngữ lạ) → lỗi của người
            // gọi, phải nói rõ ra. 5xx / mạng mới là "provider đang hỏng".
            throw GoogleApiCall.ToException("Google TTS", response.StatusCode, json);
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("audioContent", out var audioContent)
            || audioContent.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Google TTS trả về không có audioContent: {json}");
        }

        return new SpeechAudio(Convert.FromBase64String(audioContent.GetString()!), Mp3ContentType);
    }

    /// <summary>Client chỉ định tên giọng thì tôn trọng; không thì chọn theo ngôn ngữ.</summary>
    private string ResolveVoice(string? requestedVoice, string? language)
    {
        if (!string.IsNullOrWhiteSpace(requestedVoice))
        {
            return requestedVoice.Trim();
        }

        return AssistantLanguage.Resolve(language) == AssistantLanguage.English
            ? _options.EnglishVoice
            : _options.VietnameseVoice;
    }

    /// <summary>
    /// "vi-VN-Wavenet-A" → "vi-VN". Google bắt buộc languageCode phải khớp tiền tố của tên
    /// giọng, nên suy ra từ chính tên giọng thay vì để hai chỗ cấu hình lệch nhau.
    /// </summary>
    private static string LanguageCodeOf(string voiceName)
    {
        var parts = voiceName.Split('-');
        return parts.Length >= 2 ? $"{parts[0]}-{parts[1]}" : "vi-VN";
    }

    /// <summary>
    /// Cắt ở ranh giới câu gần nhất để khỏi tắt tiếng giữa chừng. Câu trả lời của hướng dẫn
    /// viên vốn đã bị prompt ép ngắn, chạm ngưỡng này là dấu hiệu prompt đang trôi.
    /// </summary>
    private string Truncate(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length <= _options.MaxCharacters)
        {
            return trimmed;
        }

        var cut = trimmed[.._options.MaxCharacters];
        var lastStop = cut.LastIndexOfAny(['.', '!', '?', '\n']);
        return lastStop > 0 ? cut[..(lastStop + 1)] : cut;
    }
}
