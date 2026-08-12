using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Assistant.Prompts;

/// <summary>
/// Đọc system prompt đang có hiệu lực, kèm mọi thứ màn quản lý cần: khối luật cứng (chỉ đọc),
/// bản gốc trong code, danh sách placeholder và lịch sử version.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record GetAssistantPromptQuery : IRequest<AssistantPromptDto>;

public sealed class GetAssistantPromptQueryHandler
    : IRequestHandler<GetAssistantPromptQuery, AssistantPromptDto>
{
    private readonly AssistantPromptProvider _provider;
    private readonly IAssistantPromptStore _store;

    public GetAssistantPromptQueryHandler(AssistantPromptProvider provider, IAssistantPromptStore store)
    {
        _provider = provider;
        _store = store;
    }

    public Task<AssistantPromptDto> Handle(GetAssistantPromptQuery request, CancellationToken cancellationToken) =>
        AssistantPromptDtoFactory.BuildAsync(_provider, _store, cancellationToken);
}
