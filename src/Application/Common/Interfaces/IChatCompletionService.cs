using System.Text.Json;

namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>
/// Trừu tượng hoá provider LLM (Gemini, Claude, ...). Toàn bộ tầng Application chỉ
/// biết đến interface này; đổi nhà cung cấp = viết class mới trong Infrastructure,
/// không đụng gì tới logic chatbox.
/// </summary>
public interface IChatCompletionService
{
    Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken);
}

public enum ChatRole
{
    User,
    Assistant,
    Tool,
}

/// <summary>
/// Một lời gọi tool do model quyết định.
/// <paramref name="Signature"/> là token suy luận opaque của provider (Gemini
/// thought_signature); phải gửi trả nguyên vẹn ở lượt sau, provider khác thì bỏ qua.
/// </summary>
public sealed record ChatToolCall(string Id, string Name, JsonElement Arguments, string? Signature = null);

/// <summary>Định nghĩa một tool cung cấp cho model (schema tham số theo chuẩn JSON Schema).</summary>
public sealed record ChatToolDefinition(string Name, string Description, JsonElement ParametersSchema);

/// <summary>Một lượt hội thoại ở dạng trung lập (không phụ thuộc provider).</summary>
public sealed record ChatMessage
{
    public ChatRole Role { get; init; }

    /// <summary>Text của lượt; với lượt Tool thì đây là JSON kết quả tool.</summary>
    public string? Text { get; init; }

    /// <summary>Các tool mà lượt Assistant yêu cầu gọi.</summary>
    public IReadOnlyList<ChatToolCall> ToolCalls { get; init; } = Array.Empty<ChatToolCall>();

    /// <summary>Chỉ dùng cho lượt Tool: khớp với ToolCall đã gọi.</summary>
    public string? ToolCallId { get; init; }

    public string? ToolName { get; init; }

    public static ChatMessage FromUser(string text) =>
        new() { Role = ChatRole.User, Text = text };

    public static ChatMessage FromAssistant(string? text, IReadOnlyList<ChatToolCall> toolCalls) =>
        new() { Role = ChatRole.Assistant, Text = text, ToolCalls = toolCalls };

    public static ChatMessage FromTool(string toolCallId, string toolName, string resultJson) =>
        new() { Role = ChatRole.Tool, Text = resultJson, ToolCallId = toolCallId, ToolName = toolName };
}

public sealed record ChatCompletionRequest(
    string SystemPrompt,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ChatToolDefinition> Tools);

public sealed record ChatCompletionResult(
    string? Text,
    IReadOnlyList<ChatToolCall> ToolCalls);
