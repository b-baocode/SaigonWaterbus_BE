using SaigonWaterbus.Application.Assistant;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Assistant : IEndpointGroup
{
    public static string RoutePrefix => "/api/assistant";

    /// <summary>Tên policy rate limit áp cho endpoint chat (đăng ký ở AddWebServices).</summary>
    public const string RateLimitPolicy = "AssistantChat";

    private const string ChatExample =
        """
        {
          "language": "VN",
          "messages": [
            { "role": "user", "text": "Mai có tàu nào từ Bạch Đằng đi Thủ Thiêm không?" }
          ]
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(Chat, "chat")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Chat voi tro ly ao Saigon Waterbus")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ChatExample,
                "Gui toan bo lich su hoi thoai trong 'messages'; role chi nhan 'user' hoac 'assistant'.",
                "language (optional): ngon ngu tra loi theo toggle cua khung chat. Nhan 'VN'/'ENG' "
                + "(dung ma FE dang dung) hoac ma ISO 'vi'/'en'/'en-US'. BO TRONG thi tro ly tu bam "
                + "theo ngon ngu khach dang viet — nen client cu khong gui field nay van chay dung.",
                "Ten ga/ben va ten tuyen luon giu nguyen tieng Viet ke ca khi tra loi tieng Anh, "
                + "vi do la ten rieng trong he thong.",
                "Kho kien thuc (chinh sach/quy dinh) tim bang KHOP TU: cau hoi tieng Anh chi khop "
                + "khi admin da khai tu khoa tieng Anh cho muc kien thuc do.",
                "Server tu goi LLM + tra cuu du lieu (ga, chuyen tau) roi tra ve cau tra loi cuoi.",
                "Chi doc du lieu, khong dat ve/thanh toan qua tro ly (v1)."));
    }

    private static async Task<IResult> Chat(ISender sender, ChatRequest request, CancellationToken ct)
    {
        var history = (request.Messages ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Text))
            .Select(m => new AssistantTurn(m.Role ?? "user", m.Text!))
            .ToArray();

        if (history.Length == 0)
        {
            return Results.BadRequest(new { error = "messages khong duoc rong." });
        }

        var reply = await sender.Send(new ChatWithAssistantCommand(history, request.Language), ct);
        return Results.Ok(new { reply = reply.Text });
    }

    /// <summary>
    /// Language optional để client cũ (chưa gửi field này) không vỡ — bỏ trống thì trợ lý tự
    /// bám theo ngôn ngữ khách viết.
    /// </summary>
    public sealed record ChatRequest(List<ChatTurnRequest>? Messages, string? Language = null);

    public sealed record ChatTurnRequest(string? Role, string? Text);
}
