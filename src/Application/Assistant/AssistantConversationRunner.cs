using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Assistant;

public enum AssistantRunStatus
{
    /// <summary>Model đã đưa ra câu trả lời cuối.</summary>
    Completed,

    /// <summary>Gọi LLM thất bại (thiếu key, quá tải, rate limit, mạng).</summary>
    ProviderFailed,

    /// <summary>Model gọi tool vòng vo quá số lần cho phép mà không chốt được câu trả lời.</summary>
    ToolLimitReached,
}

/// <param name="Text">Câu trả lời cuối. Chỉ có nghĩa khi Status là Completed.</param>
public sealed record AssistantRunResult(string? Text, AssistantRunStatus Status);

/// <summary>
/// Vòng lặp gọi LLM ↔ chạy tool, dùng chung cho mọi kiểu trợ lý: chatbox text
/// (<see cref="ChatWithAssistantCommand"/>) và hướng dẫn viên bằng giọng nói.
///
/// VÌ SAO TÁCH RA: hai luồng chỉ khác nhau ở system prompt và cách diễn đạt câu lỗi. Nếu
/// copy-paste vòng lặp thì hai bản sẽ lệch nhau sau vài tuần sửa chữa.
///
/// Trả về <see cref="AssistantRunStatus"/> thay vì tự chọn sẵn câu xin lỗi, vì hai nơi cần
/// giọng văn khác nhau — câu đọc lên cho khách NGHE phải ngắn hơn câu hiện trong khung chat.
///
/// LƯU Ý BẢO MẬT: chỉ nhận lượt user/assistant từ client. KHÔNG nhận tool_call/tool_result
/// từ client — nếu không, khách có thể chèn kết quả tool giả và lừa model. Các lượt tool được
/// sinh và tiêu thụ hoàn toàn trong server.
/// </summary>
public sealed class AssistantConversationRunner
{
    /// <summary>Chặn vòng lặp tool vô hạn (mỗi vòng là một lần gọi LLM tốn phí).</summary>
    private const int MaxToolIterations = 6;

    private readonly IChatCompletionService _chat;
    private readonly AssistantToolset _tools;
    private readonly ILogger<AssistantConversationRunner> _logger;

    public AssistantConversationRunner(
        IChatCompletionService chat,
        AssistantToolset tools,
        ILogger<AssistantConversationRunner> logger)
    {
        _chat = chat;
        _tools = tools;
        _logger = logger;
    }

    /// <param name="allowedTools">
    /// Giới hạn tool cho luồng đang chạy; null = mở hết. Hướng dẫn viên giọng nói chỉ mở 4 tool
    /// (xem <see cref="TourGuide.AskTourGuideCommandHandler"/>): mỗi định nghĩa tool đi kèm TẤT CẢ
    /// các vòng gọi LLM, mà độ trễ là điểm yếu nhất của luồng nói.
    /// </param>
    /// <param name="runContext">
    /// Ngữ cảnh lượt chat (form đặt vé đang mở) cho các tool cần nó, và là chỗ tool ghi lại thay
    /// đổi cần trả về FE. Luồng không có form thì để null.
    /// </param>
    public async Task<AssistantRunResult> RunAsync(
        string systemPrompt,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? allowedTools = null,
        AssistantRunContext? runContext = null)
    {
        var tools = _tools.DefinitionsFor(allowedTools);
        var messages = new List<ChatMessage>(history);

        for (var i = 0; i < MaxToolIterations; i++)
        {
            ChatCompletionResult result;
            try
            {
                result = await _chat.CompleteAsync(
                    new ChatCompletionRequest(systemPrompt, messages, tools),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw; // client hủy / timeout thật — để nguyên.
            }
            catch (Exception ex)
            {
                // Lỗi provider thì trả trạng thái để phía trên xin lỗi khách tử tế thay vì 500,
                // nhưng PHẢI log để còn biết nguyên nhân (đừng nuốt im lặng).
                _logger.LogError(ex, "Assistant LLM call failed");
                return new AssistantRunResult(null, AssistantRunStatus.ProviderFailed);
            }

            if (result.ToolCalls.Count == 0)
            {
                return new AssistantRunResult(result.Text ?? string.Empty, AssistantRunStatus.Completed);
            }

            messages.Add(ChatMessage.FromAssistant(result.Text, result.ToolCalls));

            foreach (var call in result.ToolCalls)
            {
                var toolResult = await _tools.ExecuteAsync(
                    call.Name, call.Arguments, allowedTools, runContext, cancellationToken);
                messages.Add(ChatMessage.FromTool(call.Id, call.Name, toolResult));
            }
        }

        _logger.LogWarning("Assistant reached the {MaxToolIterations}-iteration tool limit", MaxToolIterations);
        return new AssistantRunResult(null, AssistantRunStatus.ToolLimitReached);
    }
}
