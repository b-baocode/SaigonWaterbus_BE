using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Assistant.Prompts;

/// <summary>
/// Bỏ hẳn bản Admin sửa, quay về prompt mặc định biên dịch trong code. Bản đang chạy vẫn được
/// sao lưu trước khi xoá, nên đây không phải thao tác mất trắng.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record ResetAssistantPromptCommand : IRequest<AssistantPromptDto>;

public sealed class ResetAssistantPromptCommandHandler
    : IRequestHandler<ResetAssistantPromptCommand, AssistantPromptDto>
{
    private readonly AssistantPromptProvider _provider;
    private readonly IAssistantPromptStore _store;

    public ResetAssistantPromptCommandHandler(AssistantPromptProvider provider, IAssistantPromptStore store)
    {
        _provider = provider;
        _store = store;
    }

    public async Task<AssistantPromptDto> Handle(
        ResetAssistantPromptCommand request,
        CancellationToken cancellationToken)
    {
        await _store.ResetAsync(cancellationToken);
        return await AssistantPromptDtoFactory.BuildAsync(_provider, _store, cancellationToken);
    }
}
