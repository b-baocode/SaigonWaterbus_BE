using System.Globalization;
using System.Text.Json;
using SaigonWaterbus.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.TourGuide;

namespace SaigonWaterbus.Web.Endpoints;

/// <summary>
/// Hướng dẫn viên AI bằng giọng nói ("phần B"). Tách hẳn khỏi <see cref="Assistant"/> để
/// không làm vỡ chatbox text đang chạy production.
///
/// HAI ENDPOINT CHỨ KHÔNG MỘT: FE hiện phụ đề ngay khi `ask` trả text rồi mới gọi `speak`,
/// nhờ vậy giấu được độ trễ TTS; và nếu TTS hỏng thì khách vẫn đọc được câu trả lời.
/// </summary>
public sealed class TourGuideVoice : IEndpointGroup
{
    public static string RoutePrefix => "/api/tour-guide";

    /// <summary>
    /// Rate limit RIÊNG, chặt hơn chatbox: mỗi lượt ở đây đốt 1 lần STT + 1 lần LLM + 1 lần TTS,
    /// trong khi chatbox chỉ tốn LLM.
    /// </summary>
    public const string RateLimitPolicy = "TourGuideVoice";

    /// <summary>
    /// Chặn cứng ngay ở tầng Web. Không có nó thì một người upload file 10 phút là bay quota —
    /// và tiền STT tính theo độ dài audio.
    /// </summary>
    private const long MaxAudioBytes = 1024 * 1024;

    /// <summary>Câu trả lời của hướng dẫn viên vốn bị prompt ép ≤3 câu; dài hơn mức này là bất thường.</summary>
    private const int MaxSpeakCharacters = 2000;

    private const string AskExample =
        """
        multipart/form-data:
          audio            = <file WAV 16kHz mono, <= 15s>
          latitude         = 10.775
          longitude        = 106.705
          heading          = 45
          currentLandmark  = Bến Bạch Đằng
          history          = [{"role":"user","text":"..."},{"role":"assistant","text":"..."}]
          language         = VN
        """;

    /// <summary>
    /// Danh muc giọng cho client (mobile/web) dựng ô chọn. Chép từ `voices.list` của Google
    /// ngày 2026-08-07 — 40 giọng tiếng Việt.
    ///
    /// Để trong code chứ không gọi `voices.list` lúc chạy vì danh sách gần như không đổi, mà
    /// thêm một vòng gọi mạng vào đúng luồng đang phải tiết kiệm từng trăm mili giây thì không
    /// đáng. Google có thêm giọng mới thì cập nhật ở đây.
    /// </summary>
    private const string VoiceCatalogNote =
        """
        voice (optional): ma giong Google. BO TRONG thi dung giong mac dinh theo language
        (VN -> vi-VN-Wavenet-A nu, ENG -> en-US-Neural2-F nu). Cac ma dung duoc:

        [Chirp3-HD] doi moi nhat, tu nhien nhat, doc gon hon han cac doi cu. Ma day du dang
        vi-VN-Chirp3-HD-<Ten>, vi du vi-VN-Chirp3-HD-Kore
          Nu : Achernar, Aoede, Autonoe, Callirrhoe, Despina, Erinome, Gacrux, Kore,
               Laomedeia, Leda, Pulcherrima, Sulafat, Vindemiatrix, Zephyr
          Nam: Achird, Algenib, Algieba, Alnilam, Charon, Enceladus, Fenrir, Iapetus,
               Orus, Puck, Rasalgethi, Sadachbia, Sadaltager, Schedar, Umbriel, Zubenelgenubi

        [Neural2]  vi-VN-Neural2-A (nu), vi-VN-Neural2-D (nam)
        [Wavenet]  vi-VN-Wavenet-A (nu), -B (nam), -C (nu), -D (nam)
        [Standard] vi-VN-Standard-A (nu), -B (nam), -C (nu), -D (nam)  — may moc nhat, re nhat

        Ma sai se tra 400 kem ly do tu Google (vi du: Voice 'abc' does not exist).
        """;

    private const string SpeakExample =
        """
        {
          "text": "Bên trái bạn là Bến Bạch Đằng, bến trung tâm ngay sát phố đi bộ Nguyễn Huệ.",
          "voice": "vi-VN-Chirp3-HD-Kore",
          "language": "VN"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(Ask, "ask")
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Hoi huong dan vien bang giong noi (audio -> text tra loi)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                AskExample,
                "Gui audio dang multipart/form-data. FE NEN encode WAV 16kHz mono bang AudioContext "
                + "truoc khi gui: dinh dang MediaRecorder mac dinh (webm tren Chrome, mp4 tren iOS "
                + "Safari) khong provider STT nao nhan duoc ca hai.",
                $"Gioi han {MaxAudioBytes / 1024}KB moi lan goi.",
                "latitude/longitude/heading la vi tri tau luc khach hoi — thieu thi tro ly khong tra "
                + "loi duoc cau hoi ve canh vat xung quanh, nhung van tra loi duoc lich tau/gia ve.",
                "heardSpeech = false nghia la khong nghe ra tieng noi nao; FE nen nhac khach noi lai "
                + "va DUNG ghi luot nay vao lich su hoi thoai.",
                "Khong sinh audio o day — goi tiep /api/tour-guide/speak voi replyText."))
            .Produces<AskResponse>();

        group.MapGet(Ping, "ping")
            .AllowAnonymous()
            .WithSummary("Danh thuc backend truoc khi khach hoi cau dau tien")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                "(khong co body)",
                "FE goi luc khach bam 'Bat dau huong dan'. Azure F1/D1-Shared ngu sau vai phut va "
                + "mat ~17s de day — voi hoi thoai giong noi thi khach se tuong app chet.",
                "KHONG rate limit: request cuc re, ma chan no chinh la chan cai minh dang muon lam."))
            .Produces<PingResponse>();

        group.MapPost(Speak, "speak")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Doc mot doan text thanh tieng (TTS)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                SpeakExample,
                "Tra ve audio/mpeg (mp3) chu khong phai JSON.",
                $"text toi da {MaxSpeakCharacters} ky tu.",
                VoiceCatalogNote));
    }

    private static async Task<IResult> Ask(
        ISender sender,
        ILogger<TourGuideVoice> logger,
        IFormFile audio,
        // Nhận số dưới dạng CHUỖI rồi tự parse, KHÔNG khai `double?`. Lý do: client gửi trường
        // rỗng (`-F 'longitude='` — Swagger UI làm đúng vậy) thì binder của minimal API cố parse
        // "" thành double và ném 400 với body RỖNG, trước cả khi vào handler. Người gọi không có
        // manh mối gì để sửa. Tự parse thì coi rỗng là "không gửi", đúng như ý nghĩa của nó.
        [FromForm] string? latitude,
        [FromForm] string? longitude,
        [FromForm] string? heading,
        [FromForm] string? currentLandmark,
        [FromForm] string? history,
        [FromForm] string? language,
        CancellationToken ct)
    {
        if (audio.Length == 0)
        {
            return Results.BadRequest(new { error = "File audio rong." });
        }

        if (audio.Length > MaxAudioBytes)
        {
            return Results.BadRequest(new
            {
                error = $"Audio qua lon ({audio.Length / 1024}KB). Toi da {MaxAudioBytes / 1024}KB — "
                      + "hay ghi am ngan hon hoac ha sample rate xuong 16kHz.",
            });
        }

        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, ct);

        var turns = ParseHistory(history);
        if (turns is null)
        {
            return Results.BadRequest(new { error = "history khong phai JSON hop le." });
        }

        if (!TryParseCoordinate(latitude, out var lat)
            || !TryParseCoordinate(longitude, out var lng)
            || !TryParseCoordinate(heading, out var head))
        {
            return Results.BadRequest(new
            {
                error = "latitude/longitude/heading phai la so (vi du 10.775). Bo trong neu khong co.",
            });
        }

        TourGuideAnswer answer;
        try
        {
            answer = await sender.Send(
                new AskTourGuideCommand(
                    buffer.ToArray(),
                    audio.ContentType,
                    lat,
                    lng,
                    head,
                    currentLandmark,
                    turns,
                    language),
                ct);
        }
        catch (Exception ex) when (ex is NotSupportedException or SpeechRequestException)
        {
            // Lỗi do REQUEST sai, không phải server hỏng: định dạng audio không đọc được, hoặc
            // nhà cung cấp từ chối tham số. Nói thẳng lý do để người gọi sửa được.
            return Results.BadRequest(new { error = ex.Message });
        }
        // Client tự huỷ (đóng tab, bấm dừng) — không phải lỗi, để nguyên cho pipeline xử lý.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        // STT chết (chưa bật API, thiếu quyền, provider lỗi, mạng) KHÔNG được thành 500 trần:
        // FE cần thông điệp để nói lại với khách thay vì treo ở trạng thái "đang nghĩ".
        //
        // PHẢI bắt cả OperationCanceledException ở đây: HttpClient hết giờ cũng ném
        // TaskCanceledException, và nếu chỉ nhìn kiểu ngoại lệ thì không phân biệt được với
        // client huỷ — dấu hiệu phân biệt duy nhất là ct.IsCancellationRequested ở trên.
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or OperationCanceledException)
        {
            logger.LogError(ex, "Tour guide speech-to-text failed");
            return Results.Json(
                new { error = "Khong nhan dang duoc giong noi luc nay. Ban thu lai sau it phut nhe." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new AskResponse(answer.Transcript, answer.ReplyText, answer.HeardSpeech));
    }

    private static async Task<IResult> Speak(
        ITextToSpeechService textToSpeech,
        ILogger<TourGuideVoice> logger,
        SpeakRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Results.BadRequest(new { error = "text khong duoc rong." });
        }

        if (request.Text.Length > MaxSpeakCharacters)
        {
            return Results.BadRequest(new
            {
                error = $"text qua dai ({request.Text.Length} ky tu). Toi da {MaxSpeakCharacters}.",
            });
        }

        SpeechAudio speech;
        try
        {
            speech = await textToSpeech.SynthesizeAsync(
                new SpeechSynthesisRequest(request.Text, request.Voice, request.Language),
                ct);
        }
        catch (SpeechRequestException ex)
        {
            // Ví dụ: voice = "string" (giá trị Swagger điền sẵn) → Google trả "Voice 'string'
            // does not exist". Đó là lỗi người gọi sửa được, đừng báo thành sự cố hệ thống.
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // client tự huỷ — không phải lỗi.
        }
        // Xem ghi chú ở Ask: HttpClient hết giờ cũng ném TaskCanceledException nên phải bắt luôn
        // OperationCanceledException, phân biệt bằng ct.IsCancellationRequested ở nhánh trên.
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or OperationCanceledException)
        {
            // TTS chết KHÔNG được thành 500 trần: FE cần biết để xuống cấp mềm — hiện phụ đề,
            // bỏ phần đọc tiếng. Câu trả lời khách vẫn đọc được.
            logger.LogError(ex, "Tour guide TTS failed");
            return Results.Json(
                new { error = "Khong doc thanh tieng duoc luc nay. Hien phu de cho khach doc." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Kèm tên file → header Content-Disposition. Nhờ nó Swagger UI hiện link "Download file"
        // thay vì nhúng trình phát: Chrome khoá "Save audio as…" khi nguồn là blob nên không có
        // header này thì không tài nào lấy file ra khỏi trang được.
        // FE không bị ảnh hưởng — bên đó đọc bằng responseType 'blob', header này bị bỏ qua.
        return Results.File(speech.Data, speech.ContentType, "tour-guide-reply.mp3");
    }

    /// <summary>
    /// Rỗng = không gửi (hợp lệ, trả true kèm null). Có chữ mà không phải số = sai thật (false).
    /// Ép <see cref="CultureInfo.InvariantCulture"/> vì client luôn gửi dấu chấm thập phân,
    /// trong khi máy chủ chạy locale Việt Nam sẽ hiểu dấu phẩy.
    /// </summary>
    private static bool TryParseCoordinate(string? raw, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Lịch sử hội thoại đi qua form nên phải là chuỗi JSON. Trả null khi JSON hỏng để
    /// endpoint phân biệt được "không gửi" (hợp lệ) với "gửi rác" (400).
    /// </summary>
    private static IReadOnlyList<TourGuideTurn>? ParseHistory(string? history)
    {
        if (string.IsNullOrWhiteSpace(history))
        {
            return [];
        }

        try
        {
            var turns = JsonSerializer.Deserialize<HistoryTurnRequest[]>(
                history,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (turns ?? [])
                .Where(t => !string.IsNullOrWhiteSpace(t.Text))
                .Select(t => new TourGuideTurn(t.Role ?? "user", t.Text!))
                .ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult Ping() => Results.Ok(new PingResponse(true));

    public sealed record PingResponse(bool Ready);

    public sealed record AskResponse(string Transcript, string ReplyText, bool HeardSpeech);

    public sealed record HistoryTurnRequest(string? Role, string? Text);

    public sealed record SpeakRequest(string Text, string? Voice = null, string? Language = null);
}
