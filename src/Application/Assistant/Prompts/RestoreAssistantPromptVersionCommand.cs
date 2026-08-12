using SaigonWaterbus.Application.Common.Interfaces;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Assistant.Prompts;

/// <summary>
/// Quay lại một bản đã lưu trước đó. Bản đang chạy cũng được sao lưu trước khi bị đè, nên
/// quay lui rồi vẫn quay lại được.
/// </summary>
/// <param name="VersionId">Mốc thời gian dạng yyyyMMddTHHmmss lấy từ danh sách version.</param>
[Authorize(Roles = "Admin")]
public sealed record RestoreAssistantPromptVersionCommand(string VersionId) : IRequest<AssistantPromptDto>;

public sealed class RestoreAssistantPromptVersionCommandValidator
    : AbstractValidator<RestoreAssistantPromptVersionCommand>
{
    public RestoreAssistantPromptVersionCommandValidator() =>
        RuleFor(x => x.VersionId).NotEmpty();
}

public sealed class RestoreAssistantPromptVersionCommandHandler
    : IRequestHandler<RestoreAssistantPromptVersionCommand, AssistantPromptDto>
{
    private readonly AssistantPromptProvider _provider;
    private readonly IAssistantPromptStore _store;

    public RestoreAssistantPromptVersionCommandHandler(
        AssistantPromptProvider provider,
        IAssistantPromptStore store)
    {
        _provider = provider;
        _store = store;
    }

    public async Task<AssistantPromptDto> Handle(
        RestoreAssistantPromptVersionCommand request,
        CancellationToken cancellationToken)
    {
        var restored = await _store.RestoreAsync(request.VersionId, cancellationToken)
            ?? throw new NotFoundException($"Khong tim thay ban luu '{request.VersionId}'.");

        // Bản cũ có thể đã hỏng theo tiêu chuẩn hiện tại (ví dụ sau này thêm placeholder bắt buộc
        // mới). Không chặn khôi phục, nhưng DTO trả về sẽ kèm Errors để màn quản lý báo đỏ.
        _ = restored;
        return await AssistantPromptDtoFactory.BuildAsync(_provider, _store, cancellationToken);
    }
}
