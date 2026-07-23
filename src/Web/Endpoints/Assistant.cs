using SaigonWaterbus.Application.Assistant;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Assistant : IEndpointGroup
{
    public static string RoutePrefix => "/api/assistant";

    private const string ChatExample =
        """
        {
          "messages": [
            { "role": "user", "text": "Mai có tàu nào từ Bạch Đằng đi Thủ Thiêm không?" }
          ]
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(Chat, "chat")
            .AllowAnonymous()
            .WithSummary("Chat voi tro ly ao Saigon Waterbus")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ChatExample,
                "Gui toan bo lich su hoi thoai trong 'messages'; role chi nhan 'user' hoac 'assistant'.",
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

        var reply = await sender.Send(new ChatWithAssistantCommand(history), ct);
        return Results.Ok(new { reply = reply.Text });
    }

    public sealed record ChatRequest(List<ChatTurnRequest>? Messages);

    public sealed record ChatTurnRequest(string? Role, string? Text);
}
