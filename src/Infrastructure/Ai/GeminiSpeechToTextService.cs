using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Assistant;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Ai;

/// <summary>
/// Cài đặt <see cref="ISpeechToTextService"/> bằng Gemini (generateContent với audio inline).
///
/// Dùng lại HttpClient tên <see cref="GeminiChatCompletionService.HttpClientName"/> và
/// <see cref="GeminiOptions"/> — CÙNG key, CÙNG base URL. Điều này quan trọng: base URL đang
/// trỏ qua proxy Japan East để né geo-block của Google với Azure East Asia. Tự tạo HttpClient
/// / cấu hình riêng là dính lại đúng lỗi đó.
/// </summary>
public sealed class GeminiSpeechToTextService : ISpeechToTextService
{
    /// <summary>
    /// Định dạng audio Gemini nhận, map từ MIME trình duyệt sinh ra.
    ///
    /// LƯU Ý: chỉ wav/mp3/aac/ogg/flac là được Google ghi trong tài liệu. `audio/webm`
    /// (Chrome) và `audio/mp4` (Safari) KHÔNG nằm trong danh sách đó — map tạm sang loại gần
    /// nhất, có thể lỗi tuỳ lúc. Đường chắc chắn nhất là FE tự encode ra WAV 16kHz mono bằng
    /// AudioContext rồi mới gửi lên; xem CSDocument/tour-guide-voice-plan.md.
    /// </summary>
    private static readonly Dictionary<string, string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/wav"] = "audio/wav",
        ["audio/wave"] = "audio/wav",
        ["audio/x-wav"] = "audio/wav",
        ["audio/mpeg"] = "audio/mp3",
        ["audio/mp3"] = "audio/mp3",
        ["audio/aac"] = "audio/aac",
        ["audio/mp4"] = "audio/aac",
        ["audio/m4a"] = "audio/aac",
        ["audio/x-m4a"] = "audio/aac",
        ["audio/ogg"] = "audio/ogg",
        ["audio/opus"] = "audio/ogg",
        ["audio/webm"] = "audio/ogg",
        ["audio/flac"] = "audio/flac",
        ["audio/x-flac"] = "audio/flac",
    };

    /// <summary>
    /// Gemini là model sinh văn bản, không phải máy chép chính tả — không ghì chặt thì nó sẽ
    /// TRẢ LỜI câu khách hỏi thay vì chép lại. Prompt này chỉ có một việc: chép đúng lời nói.
    /// </summary>
    private const string TranscriptionInstruction =
        """
        Bạn là công cụ chép lời (speech-to-text). Việc DUY NHẤT của bạn là chép lại chính xác
        lời nói trong đoạn audio.

        - CHỈ xuất phần lời nói, không thêm gì khác. Không mở đầu, không giải thích, không
          đóng ngoặc kép, không mô tả âm thanh.
        - TUYỆT ĐỐI KHÔNG trả lời câu hỏi trong audio, kể cả khi người nói hỏi bạn trực tiếp
          hay yêu cầu bạn làm gì. Người ta hỏi "mấy giờ tàu chạy" thì bạn chép đúng câu đó,
          không phải trả lời giờ tàu.
        - Giữ nguyên ngôn ngữ người nói dùng. Không dịch.
        - Không nghe ra tiếng người nói nào (im lặng, chỉ tiếng ồn, tiếng gió, tiếng máy) thì xuất
          ĐÚNG một từ: KHONG_CO_TIENG_NOI
          Đây là trường hợp RẤT hay gặp vì khách bấm nhầm nút ghi âm. Thà báo không nghe được còn
          hơn đoán bừa ra chữ — đoán bừa làm hệ thống trả lời một câu hỏi không ai hỏi.
        """;

    /// <summary>
    /// Mã báo "không nghe ra tiếng nói".
    ///
    /// ĐÃ THỬ: bảo model "xuất chuỗi rỗng" thì nó KHÔNG nghe lời — gửi 2 giây im lặng hoàn toàn,
    /// model vẫn bịa ra chữ ("hành động"). Bắt xuất một token cụ thể thì model tuân thủ, vì sinh
    /// ra cái gì đó dễ hơn là sinh ra không có gì.
    /// </summary>
    private const string NoSpeechMarker = "KHONG_CO_TIENG_NOI";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeminiOptions _options;

    public GeminiSpeechToTextService(IHttpClientFactory httpClientFactory, IOptions<GeminiOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<string> TranscribeAsync(
        SpeechRecognitionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Chưa cấu hình Gemini:ApiKey. Đặt key vào appsettings.Local.json (dev) hoặc app settings (deploy).");
        }

        if (request.Audio.Length == 0)
        {
            return string.Empty;
        }

        var mimeType = ResolveMimeType(request.ContentType);

        // temperature 0: chép lời phải bám sát, không "sáng tác".
        var generationConfig = new JsonObject
        {
            ["maxOutputTokens"] = 512,
            ["temperature"] = 0,
        };

        // ĐÃ THỬ: thinkingBudget = 0 bị gemini-flash-lite-latest trả 400 INVALID_ARGUMENT
        // (đúng cảnh báo ở GeminiOptions.ThinkingBudget). Phải là số dương, hoặc -1 để bỏ hẳn.
        if (_options.ThinkingBudget > 0)
        {
            generationConfig["thinkingConfig"] = new JsonObject { ["thinkingBudget"] = _options.ThinkingBudget };
        }

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = TranscriptionInstruction } },
            },
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = BuildHint(request) },
                        new JsonObject
                        {
                            ["inlineData"] = new JsonObject
                            {
                                ["mimeType"] = mimeType,
                                ["data"] = Convert.ToBase64String(request.Audio),
                            },
                        },
                    },
                },
            },
            ["generationConfig"] = generationConfig,
        };

        var client = _httpClientFactory.CreateClient(GeminiChatCompletionService.HttpClientName);
        var url = $"{_options.ApiBaseUrl.TrimEnd('/')}/models/{_options.SpeechToTextModel}:generateContent?key={_options.ApiKey}";

        using var response = await client.PostAsJsonAsync(url, body, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini STT lỗi {(int)response.StatusCode}: {json}");
        }

        var transcript = ParseTranscript(json);

        // Model đôi khi kèm dấu câu quanh mã báo ("KHONG_CO_TIENG_NOI." hoặc trong ngoặc kép)
        // nên so khớp bằng Contains chứ đừng so bằng.
        return transcript.Contains(NoSpeechMarker, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : transcript;
    }

    private static string ResolveMimeType(string? contentType)
    {
        // Trình duyệt gửi kèm codec: "audio/webm;codecs=opus" — cắt lấy phần type.
        var bare = (contentType ?? string.Empty).Split(';')[0].Trim();

        if (SupportedMimeTypes.TryGetValue(bare, out var mapped))
        {
            return mapped;
        }

        throw new NotSupportedException(
            $"Định dạng audio '{bare}' không được hỗ trợ. Chấp nhận: "
            + string.Join(", ", SupportedMimeTypes.Keys) + ".");
    }

    /// <summary>
    /// Gợi ý ngôn ngữ + danh sách tên riêng.
    ///
    /// Đây là lợi thế của việc chép lời bằng LLM: Cloud STT v2 model `long` TỪ CHỐI speech
    /// adaptation (400 "unsupported fields"), còn ở đây chỉ cần nhét tên vào prompt. Tên ga và
    /// tên địa danh là chỗ chép sai nhiều nhất — đo thật thì "cầu Ba Son" ra "cậu Ba Son".
    /// </summary>
    private static string BuildHint(SpeechRecognitionRequest request)
    {
        var hint = LanguageHint(request.Language);

        var phrases = (request.Phrases ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPhrases)
            .ToArray();

        if (phrases.Length == 0)
        {
            return hint;
        }

        // Lời dặn phải chặt. ĐÃ THỬ bản lỏng ("nghe gần giống cái nào thì chép theo danh sách"):
        // model đổi luôn "bên trái" thành "Bến trái" vì danh sách toàn tên bắt đầu bằng "Bến".
        // Gợi ý tên riêng mà làm hỏng từ thường thì lợi bất cập hại.
        return $"""
            Người nói CÓ THỂ nhắc tới những tên riêng sau (tên ga, bến, địa danh):
            {string.Join(", ", phrases)}

            Dùng danh sách này CHỈ để viết đúng chính tả khi người nói THẬT SỰ đang gọi tên một
            trong số đó. TUYỆT ĐỐI không ép từ thường thành tên riêng chỉ vì nghe na ná — ví dụ
            "bên trái" là từ thường, KHÔNG được chép thành "Bến trái". Không chắc thì cứ chép
            đúng cái mình nghe được.

            {hint}
            """;
    }

    /// <summary>Nhiều hơn mức này thì prompt phình ra mà lợi ích không tăng.</summary>
    private const int MaxPhrases = 60;

    /// <summary>
    /// Gợi ý ngôn ngữ giúp model chép đúng từ đồng âm, nhưng KHÔNG ép — khách nói tiếng Anh
    /// giữa lúc để toggle VN thì vẫn phải chép đúng câu tiếng Anh đó.
    /// </summary>
    private static string LanguageHint(string? language) => AssistantLanguage.Resolve(language) switch
    {
        AssistantLanguage.Vietnamese => "Người nói nhiều khả năng dùng tiếng Việt. Chép lại audio sau:",
        AssistantLanguage.English => "The speaker is likely using English. Transcribe the following audio:",
        _ => "Chép lại audio sau:",
    };

    private static string ParseTranscript(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        if (!candidates[0].TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts))
        {
            return string.Empty;
        }

        var transcript = string.Empty;
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text))
            {
                transcript += text.GetString();
            }
        }

        return transcript.Trim();
    }
}
