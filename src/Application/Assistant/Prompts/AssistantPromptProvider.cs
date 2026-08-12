using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Assistant.Prompts;

/// <param name="Source">"file" = bản Admin đã sửa, "builtin" = bản mặc định biên dịch sẵn.</param>
/// <param name="Errors">
/// Lỗi của nội dung đang lưu (bình thường là rỗng). Có lỗi nghĩa là file bị sửa tay hỏng —
/// trợ lý vẫn chạy bằng bản mặc định, còn màn quản lý hiện lỗi để Admin sửa lại.
/// </param>
public sealed record AssistantPromptState(
    string Content,
    string Source,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<string> Errors)
{
    public const string FileSource = "file";
    public const string BuiltinSource = "builtin";

    public bool IsUsable => Errors.Count == 0;
}

/// <summary>
/// Nguồn duy nhất sinh ra system prompt cho chatbox.
///
/// KHÔNG CACHE — cố ý: đọc một file ~8 KB tốn vài chục micro giây, trong khi mỗi lượt chat tốn
/// 1-3 GIÂY gọi LLM. Cache chỉ mua thêm bug "sửa xong mà chưa thấy đổi" và phải đi vô hiệu hoá
/// cache ở 4 chỗ ghi khác nhau.
///
/// MỌI đường thất bại đều rơi về <see cref="AssistantPromptTemplate.Default"/>: file mất, đĩa lỗi,
/// nội dung sai placeholder. Sửa prompt hỏng thì chatbox vẫn sống.
/// </summary>
public sealed class AssistantPromptProvider
{
    private readonly IAssistantPromptStore _store;
    private readonly ILogger<AssistantPromptProvider> _logger;

    public AssistantPromptProvider(IAssistantPromptStore store, ILogger<AssistantPromptProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>Phần prompt sửa được đang có hiệu lực, kèm nguồn và lỗi (nếu có).</summary>
    public async Task<AssistantPromptState> GetStateAsync(CancellationToken cancellationToken)
    {
        AssistantPromptFile? stored;
        try
        {
            stored = await _store.ReadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not read the assistant prompt file; falling back to the built-in prompt");
            return Builtin();
        }

        if (stored is null)
        {
            return Builtin();
        }

        var errors = AssistantPromptTemplate.Validate(stored.Content);
        return new AssistantPromptState(
            stored.Content,
            AssistantPromptState.FileSource,
            stored.UpdatedAt,
            errors);
    }

    /// <summary>Prompt hoàn chỉnh gửi cho LLM: phần sửa được (đã thay placeholder) + luật cứng.</summary>
    public async Task<string> BuildSystemPromptAsync(
        DateOnly today,
        string languageInstruction,
        string? draftSummary,
        CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(cancellationToken);

        var editable = state.Content;
        if (!state.IsUsable)
        {
            // Không ném: khách đang chat không liên quan gì tới việc admin lưu hỏng file.
            _logger.LogWarning(
                "Stored assistant prompt is invalid ({Errors}); using the built-in prompt instead",
                string.Join("; ", state.Errors));
            editable = AssistantPromptTemplate.Default;
        }

        return AssistantPromptTemplate.Render(editable, today, languageInstruction, draftSummary);
    }

    private static AssistantPromptState Builtin() =>
        new(AssistantPromptTemplate.Default, AssistantPromptState.BuiltinSource, null, []);
}
