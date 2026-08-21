using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Landmarks;
using SaigonWaterbus.Application.Stations;

namespace SaigonWaterbus.Application.TourGuide;

/// <summary>Một lượt hội thoại gửi lên từ client. Role chỉ nhận "user" hoặc "assistant".</summary>
public sealed record TourGuideTurn(string Role, string Text);

/// <param name="Transcript">Câu hệ thống nghe được — FE hiện lại để khách biết máy nghe đúng chưa.</param>
/// <param name="HeardSpeech">
/// false nghĩa là không nghe ra tiếng nói nào. FE nên nhắc khách nói lại chứ đừng ghi lượt này
/// vào lịch sử hội thoại.
/// </param>
/// <param name="ExpiresAt">
/// Hạn của phiên, để client đếm ngược. Null nghĩa là không xác định được (chuyến chưa có lịch
/// từng bến, hoặc người hỏi là admin đang thử) — client cứ ẩn đồng hồ đi.
/// </param>
public sealed record TourGuideAnswer(
    string Transcript,
    string ReplyText,
    bool HeardSpeech,
    DateTimeOffset? ExpiresAt = null);

/// <summary>
/// Một lượt hỏi đáp bằng giọng nói với hướng dẫn viên: audio khách nói → chép lời → hỏi LLM
/// (có tool) → text trả lời. KHÔNG sinh audio ở đây — việc đọc thành tiếng tách sang endpoint
/// riêng để FE hiện phụ đề ngay khi có text, và để TTS hỏng thì vẫn còn chữ mà đọc.
/// Xem CSDocument/tour-guide-voice-plan.md.
///
/// Phần suy nghĩ nằm ở <see cref="TourGuideResponder"/>, dùng chung với bản gõ chữ
/// (<see cref="AskTourGuideTextCommand"/>).
/// </summary>
/// <param name="Latitude">
/// Vị trí tàu lúc khách hỏi — cần để trả lời "toà nhà kia là gì". Bỏ trống mà có
/// <paramref name="TripId"/> thì lấy bản tin GPS mới nhất của chuyến.
/// </param>
/// <param name="Heading">Hướng mũi tàu (độ, 0 = Bắc). Không có thì không nói trái/phải được.</param>
/// <param name="CurrentLandmarkName">
/// Địa danh đang/vừa được thuyết minh, nếu có. Cho phép khách hỏi tiếp "kể thêm về chỗ đó đi"
/// mà không phải nói lại tên. Chỉ dùng khi client không có sẵn id.
/// </param>
/// <param name="TripId">
/// Chuyến khách đang đi. Không có nó thì hướng dẫn viên không biết tàu này chạy tuyến nào, còn
/// ghé bến nào, mấy giờ tới nơi — mà đó là những câu hỏi thường gặp nhất trên tàu.
/// </param>
/// <param name="CurrentLandmarkId">
/// Địa danh đang/vừa được thuyết minh. Chính xác hơn <paramref name="CurrentLandmarkName"/> vì
/// hệ thống lấy được đúng lời mô tả đã duyệt thay vì để model tự nhớ.
/// </param>
public sealed record AskTourGuideCommand(
    byte[] Audio,
    string ContentType,
    double? Latitude = null,
    double? Longitude = null,
    double? Heading = null,
    string? CurrentLandmarkName = null,
    IReadOnlyList<TourGuideTurn>? History = null,
    string? Language = null,
    Guid? TripId = null,
    Guid? CurrentLandmarkId = null) : IRequest<TourGuideAnswer>;

public sealed class AskTourGuideCommandValidator : AbstractValidator<AskTourGuideCommand>
{
    public AskTourGuideCommandValidator(TourGuideAccessOptions accessOptions)
    {
        RuleFor(x => x.Audio)
            .NotEmpty().WithMessage("Chưa có dữ liệu âm thanh.");

        // BẮT BUỘC từ khi có chặn cửa theo vé: bỏ trống tripId là bỏ trống luôn câu hỏi "khách
        // này đang đi chuyến nào", tức là đi vòng qua cửa.
        RuleFor(x => x.TripId)
            .NotEmpty().WithMessage("Thiếu tripId — chưa biết bạn đang đi chuyến nào.")
            .When(_ => accessOptions.Enabled);

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("Thiếu định dạng âm thanh (contentType).");

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue)
            .WithMessage("Vĩ độ phải nằm trong khoảng -90 đến 90.");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue)
            .WithMessage("Kinh độ phải nằm trong khoảng -180 đến 180.");
    }
}

public sealed class AskTourGuideCommandHandler : IRequestHandler<AskTourGuideCommand, TourGuideAnswer>
{
    private readonly ISpeechToTextService _speechToText;
    private readonly TourGuideResponder _responder;
    private readonly TourGuideAccessSupport _access;
    private readonly ISender _sender;

    public AskTourGuideCommandHandler(
        ISpeechToTextService speechToText,
        TourGuideResponder responder,
        TourGuideAccessSupport access,
        ISender sender)
    {
        _speechToText = speechToText;
        _responder = responder;
        _access = access;
        _sender = sender;
    }

    public async Task<TourGuideAnswer> Handle(AskTourGuideCommand request, CancellationToken cancellationToken)
    {
        // Gác cửa TRƯỚC mọi thứ khác: STT và LLM đều tốn tiền, không có lý do gì tiêu trước
        // rồi mới hỏi người này có được dùng không.
        var access = await _access.EvaluateAsync(request.TripId, cancellationToken);
        if (!access.Allowed)
        {
            throw new TourGuideAccessDeniedException(access.ReasonCode);
        }

        // Chặn im lặng TRƯỚC khi gọi STT: model sẽ bịa ra câu hỏi nếu không nghe thấy gì
        // (xem SilentAudioDetector). Chặn ở đây cũng tiết kiệm 1 lần STT + 1 lần LLM.
        if (SilentAudioDetector.IsSilent(request.Audio, request.ContentType))
        {
            return NotHeard(access.ExpiresAt);
        }

        var transcript = await _speechToText.TranscribeAsync(
            new SpeechRecognitionRequest(
                request.Audio,
                request.ContentType,
                request.Language,
                await BuildPhraseHintsAsync(cancellationToken)),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(transcript))
        {
            // Không nghe ra gì thì ĐỪNG gọi LLM — tốn tiền, tốn thời gian, và model sẽ bịa ra
            // một câu hỏi không ai hỏi.
            return NotHeard(access.ExpiresAt);
        }

        var reply = await _responder.AnswerAsync(
            new TourGuideAsk(
                transcript,
                request.Latitude,
                request.Longitude,
                request.Heading,
                request.CurrentLandmarkName,
                request.History,
                request.Language,
                request.TripId,
                request.CurrentLandmarkId),
            cancellationToken);

        return new TourGuideAnswer(transcript, reply, HeardSpeech: true, access.ExpiresAt);
    }

    /// <summary>
    /// Tên ga và tên địa danh, bơm xuống bộ nhận dạng làm gợi ý. Đây là chỗ sai nhiều nhất khi
    /// chép lời: đo thật thì "cầu Ba Son" ra "cậu Ba Son".
    ///
    /// Hai bảng này rất nhỏ (chục dòng) nên đọc mỗi lượt cũng không đáng kể so với 2–4 giây của
    /// chính lời gọi STT. Khi nào dữ liệu phình lên thì mới cần cache.
    ///
    /// Hỏng thì bỏ qua chứ không làm chết cả lượt hỏi — thà chép sai tên riêng còn hơn không
    /// nghe được gì.
    /// </summary>
    private async Task<IReadOnlyList<string>> BuildPhraseHintsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stations = await _sender.Send(new GetStationListQuery(), cancellationToken);
            var landmarks = await _sender.Send(new GetLandmarksQuery(null), cancellationToken);

            return stations.Select(s => s.StationName)
                .Concat(landmarks.Select(l => l.LandmarkName))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static TourGuideAnswer NotHeard(DateTimeOffset? expiresAt) =>
        new(string.Empty, "Mình chưa nghe rõ, bạn nói lại giúp mình nhé.", HeardSpeech: false, expiresAt);
}
