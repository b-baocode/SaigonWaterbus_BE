using SaigonWaterbus.Application.Assistant.Prompts;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Assistant;

/// <summary>Một lượt hội thoại gửi lên từ client. Role chỉ nhận "user" hoặc "assistant".</summary>
public sealed record AssistantTurn(string Role, string Text);

/// <param name="DraftPatch">
/// Thay đổi trợ lý muốn ghi vào form đặt vé đang mở (hiện chỉ có: ghế đã chọn hộ khách).
/// Null = lượt này không đụng vào form.
/// </param>
public sealed record AssistantReply(
    string Text,
    IReadOnlyList<string>? SuggestedQuestions = null,
    IReadOnlyList<AssistantAction>? Actions = null,
    AssistantDraftPatch? DraftPatch = null);

/// <summary>
/// Điều phối một lượt trả lời của trợ lý ảo: chạy vòng lặp gọi LLM ↔ chạy tool cho
/// tới khi model đưa ra câu trả lời cuối (không còn tool call).
///
/// LƯU Ý BẢO MẬT: chỉ nhận text của lượt user/assistant từ client. KHÔNG nhận
/// tool_call / tool_result từ client — nếu không, khách có thể chèn kết quả tool giả
/// và lừa model. Các lượt tool được sinh và tiêu thụ hoàn toàn trong server.
/// </summary>
/// <param name="Language">
/// Ngôn ngữ khách chọn ở toggle của khung chat ("VN"/"ENG", hoặc mã ISO "vi"/"en").
/// Bỏ trống thì trợ lý tự bám theo ngôn ngữ trong tin nhắn của khách.
/// </param>
/// <param name="BookingDraftJson">
/// Trạng thái form đặt vé nhúng trong chat (JSON thô do FE gửi kèm request). Dùng để trợ lý biết
/// khách đang ở bước nào mà không hỏi lại. Chỉ được TRÍCH có chọn lọc qua
/// <see cref="AssistantBookingDraftSummary"/> (cho model đọc) và <see cref="AssistantBookingDraftReader"/>
/// (cho tool dùng) — tuyệt đối không nhét thẳng vào prompt, vì đây là dữ liệu do client kiểm soát.
/// </param>
public sealed record ChatWithAssistantCommand(
    IReadOnlyList<AssistantTurn> History,
    string? Language = null,
    string? BookingDraftJson = null) : IRequest<AssistantReply>;

public sealed class ChatWithAssistantCommandHandler
    : IRequestHandler<ChatWithAssistantCommand, AssistantReply>
{
    /// <summary>Chỉ giữ lại N lượt gần nhất khi gửi cho LLM để khỏi phình token.</summary>
    private const int MaxHistoryTurns = 8;

    private readonly AssistantConversationRunner _runner;
    private readonly AssistantPromptProvider _prompts;
    private readonly TimeProvider _timeProvider;

    public ChatWithAssistantCommandHandler(
        AssistantConversationRunner runner,
        AssistantPromptProvider prompts,
        TimeProvider timeProvider)
    {
        _runner = runner;
        _prompts = prompts;
        _timeProvider = timeProvider;
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

        // Giờ Việt Nam (UTC+7): đưa ngày hôm nay vào prompt để model tự quy đổi "mai",
        // "thứ 7 tuần sau"... sang yyyy-MM-dd khi gọi tool.
        // Nội dung prompt do Admin sửa được (xem AssistantPromptProvider), nên KHÔNG viết thẳng ở đây.
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime.AddHours(7));
        var systemPrompt = await _prompts.BuildSystemPromptAsync(
            today,
            AssistantLanguage.PromptInstruction(AssistantLanguage.Resolve(request.Language)),
            AssistantBookingDraftSummary.Build(request.BookingDraftJson),
            cancellationToken);

        // Draft đi vào hai đường tách biệt và CỐ Ý không trộn: bản tóm tắt đã làm sạch ở trên cho
        // model ĐỌC, còn runContext cho tool DÙNG (id chuyến, chặng, số ghế). Model không nhìn thấy
        // phần sau nên không bịa được chuyến khác để chọn ghế hộ.
        var runContext = new AssistantRunContext(AssistantBookingDraftReader.Read(request.BookingDraftJson));
        var result = await _runner.RunAsync(systemPrompt, messages, cancellationToken, runContext: runContext);

        // Vòng lặp LLM↔tool nằm ở AssistantConversationRunner (dùng chung với hướng dẫn viên
        // giọng nói); ở đây chỉ chọn cách diễn đạt cho khung chat text.
        var text = result.Status switch
        {
            AssistantRunStatus.Completed => result.Text ?? string.Empty,
            AssistantRunStatus.ProviderFailed =>
                "Xin lỗi, trợ lý đang bận. Bạn vui lòng thử lại sau ít phút nhé.",
            _ => "Xin lỗi, mình chưa xử lý được yêu cầu này. Bạn thử hỏi lại theo cách khác nhé.",
        };

        // Mọi đường ra đều đi qua BuildReply để câu gợi ý + nút hành động không bị rơi mất.
        return BuildReply(request, text, runContext.DraftPatch);
    }

    private static AssistantReply BuildReply(
        ChatWithAssistantCommand request,
        string text,
        AssistantDraftPatch? draftPatch)
    {
        var question = request.History.LastOrDefault(x => string.Equals(x.Role, "user", StringComparison.OrdinalIgnoreCase))?.Text ?? string.Empty;
        var suggestions = AssistantSuggestions.Build(question, request.Language);
        return new AssistantReply(text, suggestions.Questions, suggestions.Actions, draftPatch);
    }
}
