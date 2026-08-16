namespace SaigonWaterbus.Application.TourGuide;

/// <summary>
/// Hỏi hướng dẫn viên bằng CHỮ thay vì giọng nói. Bỏ qua chặn im lặng và STT, phần còn lại
/// (ngữ cảnh chuyến, system prompt, tool, luật trả lời) y hệt <see cref="AskTourGuideCommand"/>
/// vì cả hai dùng chung <see cref="TourGuideResponder"/>.
///
/// VÌ SAO CÓ: tinh chỉnh prompt mà mỗi lần thử phải thu âm một câu WAV 16kHz thì không ai làm
/// nổi — mà đây lại đúng là thứ cần thử đi thử lại nhiều nhất. Gõ chữ cho ra ĐÚNG câu trả lời
/// khách sẽ nghe, chỉ khác đường vào.
///
/// Dùng được cả cho FE nếu sau này muốn có ô "gõ câu hỏi" bên cạnh nút mic: trên tàu ồn, mic
/// hỏng, hoặc khách ngại nói giữa đám đông.
/// </summary>
/// <param name="Text">Câu hỏi của khách. Xem <paramref name="Text"/> ở validator cho giới hạn độ dài.</param>
public sealed record AskTourGuideTextCommand(
    string Text,
    double? Latitude = null,
    double? Longitude = null,
    double? Heading = null,
    string? CurrentLandmarkName = null,
    IReadOnlyList<TourGuideTurn>? History = null,
    string? Language = null,
    Guid? TripId = null,
    Guid? CurrentLandmarkId = null) : IRequest<string>;

public sealed class AskTourGuideTextCommandValidator : AbstractValidator<AskTourGuideTextCommand>
{
    /// <summary>
    /// Một câu nói 15 giây (trần của đường giọng nói) rơi vào khoảng 250–300 ký tự. Để 1000 cho
    /// rộng rãi, nhưng vẫn phải có trần: không có nó thì dán nguyên một cuốn sách vào là đốt
    /// token của cả hệ thống.
    /// </summary>
    private const int MaxTextLength = 1000;

    public AskTourGuideTextCommandValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("Chưa nhập câu hỏi.")
            .MaximumLength(MaxTextLength)
            .WithMessage($"Câu hỏi quá dài, tối đa {MaxTextLength} ký tự.");

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue)
            .WithMessage("Vĩ độ phải nằm trong khoảng -90 đến 90.");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue)
            .WithMessage("Kinh độ phải nằm trong khoảng -180 đến 180.");
    }
}

public sealed class AskTourGuideTextCommandHandler : IRequestHandler<AskTourGuideTextCommand, string>
{
    private readonly TourGuideResponder _responder;

    public AskTourGuideTextCommandHandler(TourGuideResponder responder) => _responder = responder;

    public Task<string> Handle(AskTourGuideTextCommand request, CancellationToken cancellationToken) =>
        _responder.AnswerAsync(
            new TourGuideAsk(
                request.Text,
                request.Latitude,
                request.Longitude,
                request.Heading,
                request.CurrentLandmarkName,
                request.History,
                request.Language,
                request.TripId,
                request.CurrentLandmarkId),
            cancellationToken);
}
