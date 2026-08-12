using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Assistant.Prompts;

public sealed record AssistantPromptPlaceholderDto(string Token, string Description);

public sealed record AssistantPromptVersionDto(string Id, DateTimeOffset CreatedAt, int Length);

/// <param name="Content">Phần prompt Admin sửa được, đang có hiệu lực.</param>
/// <param name="Source">"file" = bản đã sửa; "builtin" = bản mặc định trong code.</param>
/// <param name="Errors">Lỗi của nội dung đang lưu; khác rỗng nghĩa là trợ lý đang chạy bản mặc định.</param>
/// <param name="LockedRules">Khối luật cứng server luôn nối vào cuối. Chỉ để đọc.</param>
/// <param name="DefaultContent">Bản gốc trong code, để so sánh và khôi phục.</param>
public sealed record AssistantPromptDto(
    string Content,
    string Source,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<string> Errors,
    string LockedRules,
    string DefaultContent,
    IReadOnlyList<AssistantPromptPlaceholderDto> Placeholders,
    int MinLength,
    int MaxLength,
    string StorageLocation,
    IReadOnlyList<AssistantPromptVersionDto> Versions);

/// <param name="Status">Completed | ProviderFailed | ToolLimitReached.</param>
/// <param name="UsedTools">Có cho trợ lý tra dữ liệu thật hay không (ảnh hưởng số lượt gọi LLM).</param>
public sealed record AssistantPromptPreviewDto(
    string Reply,
    string Status,
    bool UsedTools,
    int PromptLength);

internal static class AssistantPromptDtoFactory
{
    public static async Task<AssistantPromptDto> BuildAsync(
        AssistantPromptProvider provider,
        IAssistantPromptStore store,
        CancellationToken cancellationToken)
    {
        var state = await provider.GetStateAsync(cancellationToken);
        var versions = await store.ListVersionsAsync(cancellationToken);

        return new AssistantPromptDto(
            state.Content,
            state.Source,
            state.UpdatedAt,
            state.Errors,
            AssistantPromptTemplate.LockedRules,
            AssistantPromptTemplate.Default,
            AssistantPromptTemplate.PlaceholderHelp
                .Select(item => new AssistantPromptPlaceholderDto($"{{{{{item.Name}}}}}", item.Description))
                .ToArray(),
            AssistantPromptTemplate.MinLength,
            AssistantPromptTemplate.MaxLength,
            store.Location,
            versions.Select(v => new AssistantPromptVersionDto(v.Id, v.CreatedAt, v.Length)).ToArray());
    }
}
