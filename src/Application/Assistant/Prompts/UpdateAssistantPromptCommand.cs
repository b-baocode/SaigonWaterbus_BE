using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Assistant.Prompts;

/// <summary>
/// Lưu phần prompt Admin sửa được. Có hiệu lực ngay từ lượt chat kế tiếp — không restart,
/// không deploy.
///
/// Bản đang chạy được sao lưu thành một version TRƯỚC khi ghi đè, nên lưu nhầm vẫn quay lui được.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record UpdateAssistantPromptCommand(string Content) : IRequest<AssistantPromptDto>;

public sealed class UpdateAssistantPromptCommandHandler
    : IRequestHandler<UpdateAssistantPromptCommand, AssistantPromptDto>
{
    private readonly AssistantPromptProvider _provider;
    private readonly IAssistantPromptStore _store;

    public UpdateAssistantPromptCommandHandler(AssistantPromptProvider provider, IAssistantPromptStore store)
    {
        _provider = provider;
        _store = store;
    }

    public async Task<AssistantPromptDto> Handle(
        UpdateAssistantPromptCommand request,
        CancellationToken cancellationToken)
    {
        // Chặn Ở ĐÂY chứ không chỉ cảnh báo lúc chạy: prompt thiếu placeholder vẫn "chạy được",
        // chỉ là trợ lý hết biết hôm nay ngày mấy — kiểu hỏng im lặng, khó truy nhất.
        var errors = AssistantPromptTemplate.Validate(request.Content);
        if (errors.Count > 0)
        {
            throw new ValidationException(
                errors.Select(error => new ValidationFailure(nameof(request.Content), error)).ToArray());
        }

        await _store.WriteAsync(request.Content.Trim(), cancellationToken);
        return await AssistantPromptDtoFactory.BuildAsync(_provider, _store, cancellationToken);
    }
}
