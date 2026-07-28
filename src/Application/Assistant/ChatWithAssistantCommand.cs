using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Assistant;

/// <summary>Một lượt hội thoại gửi lên từ client. Role chỉ nhận "user" hoặc "assistant".</summary>
public sealed record AssistantTurn(string Role, string Text);

public sealed record AssistantReply(string Text);

/// <summary>
/// Điều phối một lượt trả lời của trợ lý ảo: chạy vòng lặp gọi LLM ↔ chạy tool cho
/// tới khi model đưa ra câu trả lời cuối (không còn tool call).
///
/// LƯU Ý BẢO MẬT: chỉ nhận text của lượt user/assistant từ client. KHÔNG nhận
/// tool_call / tool_result từ client — nếu không, khách có thể chèn kết quả tool giả
/// và lừa model. Các lượt tool được sinh và tiêu thụ hoàn toàn trong server.
/// </summary>
public sealed record ChatWithAssistantCommand(IReadOnlyList<AssistantTurn> History)
    : IRequest<AssistantReply>;

public sealed class ChatWithAssistantCommandHandler
    : IRequestHandler<ChatWithAssistantCommand, AssistantReply>
{
    /// <summary>Chặn vòng lặp tool vô hạn (mỗi vòng là một lần gọi LLM tốn phí).</summary>
    private const int MaxToolIterations = 6;

    /// <summary>Chỉ giữ lại N lượt gần nhất khi gửi cho LLM để khỏi phình token.</summary>
    private const int MaxHistoryTurns = 8;

    private readonly IChatCompletionService _chat;
    private readonly AssistantToolset _tools;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChatWithAssistantCommandHandler> _logger;

    public ChatWithAssistantCommandHandler(
        IChatCompletionService chat,
        AssistantToolset tools,
        TimeProvider timeProvider,
        ILogger<ChatWithAssistantCommandHandler> logger)
    {
        _chat = chat;
        _tools = tools;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AssistantReply> Handle(ChatWithAssistantCommand request, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();
        foreach (var turn in request.History.TakeLast(MaxHistoryTurns))
        {
            messages.Add(string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? ChatMessage.FromAssistant(turn.Text, Array.Empty<ChatToolCall>())
                : ChatMessage.FromUser(turn.Text));
        }

        var systemPrompt = BuildSystemPrompt();

        for (var i = 0; i < MaxToolIterations; i++)
        {
            ChatCompletionResult result;
            try
            {
                result = await _chat.CompleteAsync(
                    new ChatCompletionRequest(systemPrompt, messages, _tools.Definitions),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw; // client hủy / timeout thật — để nguyên.
            }
            catch (Exception ex)
            {
                // LLM lỗi (thiếu key, quá tải, rate limit provider, mạng) — trả lời lịch sự
                // thay vì 500, nhưng PHẢI log để còn biết nguyên nhân (đừng nuốt im lặng).
                _logger.LogError(ex, "Assistant LLM call failed");
                return new AssistantReply(
                    "Xin lỗi, trợ lý đang bận. Bạn vui lòng thử lại sau ít phút nhé.");
            }

            if (result.ToolCalls.Count == 0)
            {
                return new AssistantReply(result.Text ?? string.Empty);
            }

            messages.Add(ChatMessage.FromAssistant(result.Text, result.ToolCalls));

            foreach (var call in result.ToolCalls)
            {
                var toolResult = await _tools.ExecuteAsync(call.Name, call.Arguments, cancellationToken);
                messages.Add(ChatMessage.FromTool(call.Id, call.Name, toolResult));
            }
        }

        return new AssistantReply(
            "Xin lỗi, mình chưa xử lý được yêu cầu này. Bạn thử hỏi lại theo cách khác nhé.");
    }

    private string BuildSystemPrompt()
    {
        // Giờ Việt Nam (UTC+7). Đặt ngày hôm nay vào prompt để model tự quy đổi
        // "mai", "thứ 7 tuần sau"... sang định dạng yyyy-MM-dd khi gọi tool.
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime.AddHours(7));

        return $"""
        Bạn là trợ lý ảo của Saigon Waterbus — hệ thống tàu buýt đường sông tại TP.HCM.
        Nhiệm vụ: giúp khách tra cứu ga, lịch chạy tàu, giờ khởi hành, chỗ trống và giá vé.

        Hôm nay là {today:yyyy-MM-dd} (giờ Việt Nam). Khách nói "mai", "thứ 7 tuần sau"...
        thì tự quy đổi sang định dạng yyyy-MM-dd trước khi gọi tool.

        Quy tắc bắt buộc:
        - CHỈ trả lời dựa trên dữ liệu do tool trả về. TUYỆT ĐỐI không bịa lịch tàu, giá vé,
          tên ga hay giờ chạy. Không biết thì nói không biết.
        - Khi khách hỏi về chuyến tàu/giờ chạy, hãy gọi tool search_trips.
        - Nếu chưa chắc tên ga, gọi list_stations để lấy danh sách ga hợp lệ.
        - Nếu tool trả về trường "error", đọc thông báo đó và hỏi lại khách cho đúng
          (ví dụ gợi ý tên ga hợp lệ) — đừng bịa kết quả.
        - Trả lời ngắn gọn, thân thiện, bằng tiếng Việt. Có thể dùng danh sách gạch đầu dòng
          cho nhiều chuyến. Không cần nhắc tới việc bạn đang gọi tool.
        """;
    }
}
