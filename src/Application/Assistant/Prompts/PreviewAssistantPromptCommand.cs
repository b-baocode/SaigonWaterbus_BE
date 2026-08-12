using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Assistant.Prompts;

/// <summary>
/// Chạy thử một câu hỏi với prompt NHÁP trước khi lưu. Không lưu hội thoại, không đụng tới
/// khách đang chat.
/// </summary>
/// <param name="WithTools">
/// false (mặc định) = trợ lý KHÔNG được tra dữ liệu → đúng 1 lần gọi LLM, đủ để kiểm giọng văn,
/// lời chào, câu từ chối ngoài phạm vi. true = chạy như thật, tốn 2-4 lần gọi LLM vì mỗi vòng
/// lặp tool là một lần gọi. Hạn mức Gemini free-tier tính theo project nên preview chạy nhiều
/// sẽ ăn vào phần của khách thật — đó là lý do mặc định tắt.
/// </param>
[Authorize(Roles = "Admin")]
public sealed record PreviewAssistantPromptCommand(
    string Content,
    string Question,
    string? Language = null,
    bool WithTools = false) : IRequest<AssistantPromptPreviewDto>;

public sealed class PreviewAssistantPromptCommandValidator
    : AbstractValidator<PreviewAssistantPromptCommand>
{
    public PreviewAssistantPromptCommandValidator()
    {
        RuleFor(x => x.Content).NotEmpty();
        RuleFor(x => x.Question).NotEmpty().MaximumLength(500);
    }
}

public sealed class PreviewAssistantPromptCommandHandler
    : IRequestHandler<PreviewAssistantPromptCommand, AssistantPromptPreviewDto>
{
    /// <summary>Bộ tool rỗng = model không gọi được gì, chỉ trả lời bằng chính prompt.</summary>
    private static readonly IReadOnlySet<string> NoTools = new HashSet<string>(StringComparer.Ordinal);

    private readonly AssistantConversationRunner _runner;
    private readonly TimeProvider _timeProvider;

    public PreviewAssistantPromptCommandHandler(
        AssistantConversationRunner runner,
        TimeProvider timeProvider)
    {
        _runner = runner;
        _timeProvider = timeProvider;
    }

    public async Task<AssistantPromptPreviewDto> Handle(
        PreviewAssistantPromptCommand request,
        CancellationToken cancellationToken)
    {
        var errors = AssistantPromptTemplate.Validate(request.Content);
        if (errors.Count > 0)
        {
            throw new ValidationException(
                errors.Select(error => new ValidationFailure(nameof(request.Content), error)).ToArray());
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime.AddHours(7));
        var systemPrompt = AssistantPromptTemplate.Render(
            request.Content,
            today,
            AssistantLanguage.PromptInstruction(AssistantLanguage.Resolve(request.Language)),
            // Không giả lập form đặt vé: preview để soi câu chữ, không phải để diễn lại luồng đặt vé.
            draftSummary: null);

        var result = await _runner.RunAsync(
            systemPrompt,
            [ChatMessage.FromUser(request.Question)],
            cancellationToken,
            allowedTools: request.WithTools ? null : NoTools);

        var reply = result.Status == AssistantRunStatus.Completed
            ? result.Text ?? string.Empty
            : string.Empty;

        return new AssistantPromptPreviewDto(
            reply,
            result.Status.ToString(),
            request.WithTools,
            systemPrompt.Length);
    }
}
