using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Assistant;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Infrastructure.Data;
using DomainChatMessage = SaigonWaterbus.Domain.Entities.ChatMessage;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Assistant : IEndpointGroup
{
    public static string RoutePrefix => "/api/assistant";
    public const string RateLimitPolicy = "AssistantChat";

    private const string ChatExample =
        """
        {
          "messages": [{ "role": "user", "text": "mai co chuyen nao tu Bach Dang khong" }],
          "language": "VN",
          "conversationId": null,
          "clientSessionId": "web-8f1c...",
          "bookingDraft": {
            "stage": "SelectingTrip",
            "serviceType": "Waterbus",
            "fromStationName": "Bạch Đằng",
            "toStationName": "Thủ Thiêm",
            "departureDate": "2026-08-20",
            "isRoundTrip": false,
            "returnDate": null,
            "adultCount": 2,
            "childCount": 1,
            "infantCount": 0,
            "selectedDepartureTrip": null,
            "selectedSeatsDeparture": []
          }
        }
        """;

    private const string BookingDraftNote =
        """
        bookingDraft (optional): trang thai form dat ve dang mo trong khung chat. LA MOT OBJECT
        JSON, khong phai chuoi — Swagger hien "string" chi vi khong sinh duoc schema cho kieu nay.

        BE chi doc dung 12 truong duoi day, moi truong con lai (departureTrips, passengers,
        contact, preview...) bi BO QUA hoan toan — dung gui de khoi ton bang thong va han muc 64KB:
          stage                   : CollectingInfo | SelectingTrip | SelectingSeats |
                                    EnteringPassengers | AwaitingConfirmation (gia tri la = bo)
          serviceType             : Waterbus | Sightseeing. BO TRONG = Waterbus. Tour ngam canh
                                    PHAI gui "Sightseeing", neu khong tro ly se di hoi ben di/ben
                                    den cho mot tour di vong.
          fromStationName         : ten ben di (cat con 60 ky tu)
          toStationName           : ten ben den (cat con 60 ky tu)
          departureDate           : yyyy-MM-dd (sai dinh dang = bo)
          isRoundTrip             : true|false
          returnDate              : yyyy-MM-dd, chi doc khi isRoundTrip = true
          adultCount              : 0..100 (ngoai khoang = bo). Nhan ca so lan chuoi.
          childCount              : 0..100
          infantCount             : 0..100
          selectedDepartureTrip   : CHI xet co hay khong, khong doc noi dung
          selectedSeatsDeparture  : CHI dem so phan tu
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(Chat, "chat")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Chat voi tro ly ao Saigon Waterbus")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous (co token thi hoi thoai gan vao tai khoan)",
                ChatExample,
                "messages: BE chi lay CAU USER CUOI CUNG trong mang nay. Lich su truoc do server tu "
                + "doc lai tu DB theo conversationId, client khong can gui lai (gui cung khong sao, "
                + "phan thua bi bo). Luot dau tien de conversationId = null, server tao roi tra ve.",
                "clientSessionId: BAT BUOC voi khach chua dang nhap — no la thu duy nhat chung minh "
                + "hoi thoai la cua minh. Gui sai/thieu o cac luot sau se bi 404. Khach da dang nhap "
                + "thi so khop theo userId, bo qua truong nay.",
                BookingDraftNote,
                "bookingDraft KHONG duoc luu tren server: no chi song trong dung luot request do. "
                + "Client PHAI tu giu (localStorage) va gui kem MOI luot, neu khong tro ly se hoi lai "
                + "tu dau sau khi khach F5 — lich su hoi thoai KHONG thay the duoc draft. Draft lon "
                + "hon 64KB tra 400.",
                "Noi dung ben trong bookingDraft la du lieu do client kiem soat nen bi coi la KHONG "
                + "dang tin: BE lam sach tung gia tri va nhet vao prompt kem cau ra lenh bo qua moi "
                + "chi dan an trong do. Dung trong cho no de dieu khien tro ly.",
                "Response: reply (text tra loi), suggestedQuestions[], actions[] (nut dieu huong), "
                + "conversationId, status (Open|Closed).",
                "409 = hoi thoai da dong, phai bat dau hoi thoai moi (conversationId = null). "
                + "404 = khong tim thay hoi thoai hoac khong phai cua minh.",
                "Rate limit 8 luot/60s theo user (hoac X-Device-Id, hoac IP neu khong co ca hai); "
                + "vuot thi 429.",
                "Hoi thoai tu dong dong sau 30 phut khong hoat dong."))
            .Produces<object>();

        group.MapGet(GetConversation, "conversations/{id:guid}")
            .AllowAnonymous()
            .WithSummary("Doc lai mot hoi thoai da co")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous (khach vang lai phai kem clientSessionId)",
                "(khong co body) — vi du: GET /api/assistant/conversations/{id}?clientSessionId=web-8f1c...",
                "Dung khi client mo lai khung chat (F5, doi tab) de dung lai cac bong bong tin nhan.",
                "KHONG tra bookingDraft — draft khong duoc luu tren server, xem note o POST /chat.",
                "isAutoCloseMessage = true la cau thong bao he thong tu chen khi dong hoi thoai, "
                + "khong phai loi tro ly noi voi khach.",
                "404 khi khong tim thay hoac hoi thoai khong thuoc ve nguoi goi."))
            .Produces<object>();

        group.MapPost(CloseConversation, "conversations/{id:guid}/close")
            .AllowAnonymous()
            .WithSummary("Dong hoi thoai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous (khach vang lai phai kem clientSessionId)",
                "(khong co body) — vi du: POST /api/assistant/conversations/{id}/close?clientSessionId=web-8f1c...",
                "Idempotent: goi lai tren hoi thoai da dong van tra 200 kem status Closed.",
                "Dong roi thi KHONG chat tiep duoc (POST /chat tra 409) — phai mo hoi thoai moi.",
                "Du lieu hoi thoai giu them 7 ngay ke tu luc dong roi moi den han xoa.",
                "404 khi khong tim thay hoac hoi thoai khong thuoc ve nguoi goi."))
            .Produces<object>();
    }

    /// <summary>
    /// Giới hạn kích thước draft nhận từ client, để một client lỗi (hoặc cố ý) không nhồi được
    /// megabyte vào prompt.
    /// </summary>
    private const int MaxBookingDraftBytes = 64 * 1024;

    private static async Task<IResult> Chat(
        ISender sender,
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ChatRequest request,
        CancellationToken ct)
    {
        var incoming = (request.Messages ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Text))
            .ToArray();
        var latestUserText = incoming.LastOrDefault(x => string.Equals(x.Role, "user", StringComparison.OrdinalIgnoreCase))?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(latestUserText))
        {
            return Results.BadRequest(new { error = "Tin nhan user khong duoc rong." });
        }

        var now = timeProvider.GetUtcNow();
        ChatConversation? conversation = null;
        if (request.ConversationId is Guid conversationId)
        {
            conversation = await context.ChatConversations
                .Include(x => x.Messages.OrderBy(m => m.SequenceNumber))
                .SingleOrDefaultAsync(x => x.Id == conversationId, ct);
            if (conversation is null || !OwnsConversation(conversation, userContext.UserId, request.ClientSessionId))
            {
                return Results.NotFound(new { error = "Khong tim thay hoi thoai." });
            }

            if (conversation.Status != ChatConversation.OpenStatus)
            {
                return Results.Conflict(new { error = "Hoi thoai da dong. Vui long bat dau hoi thoai moi." });
            }
        }
        else
        {
            conversation = new ChatConversation
            {
                UserId = userContext.UserId,
                AnonymousSessionId = userContext.UserId is null ? NormalizeSession(request.ClientSessionId) : null,
                Language = string.IsNullOrWhiteSpace(request.Language) ? "VN" : request.Language!,
                StartedAt = now,
                LastActivityAt = now,
                LastAssistantMessageAt = now,
                InactivityDeadlineAt = now.AddMinutes(30)
            };
            context.ChatConversations.Add(conversation);
        }

        var history = conversation.Messages
            .Where(x => !x.IsAutoCloseMessage && (x.Role == DomainChatMessage.UserRole || x.Role == DomainChatMessage.AssistantRole))
            .OrderBy(x => x.SequenceNumber)
            .Select(x => new AssistantTurn(x.Role, x.Content))
            .Append(new AssistantTurn(DomainChatMessage.UserRole, latestUserText))
            .ToArray();

     
        var incomingDraft = NormalizeBookingDraft(request.BookingDraft);
        if (incomingDraft is not null && incomingDraft.Length > MaxBookingDraftBytes)
        {
            return Results.BadRequest(new { error = "Booking draft qua lon." });
        }

        var reply = await sender.Send(
            new ChatWithAssistantCommand(
                history,
                request.Language ?? conversation.Language,
                incomingDraft),
            ct);
        var nextSequence = conversation.Messages.Count == 0
            ? 1
            : conversation.Messages.Max(x => x.SequenceNumber) + 1;
        context.ChatMessages.Add(new DomainChatMessage
        {
            ConversationId = conversation.Id,
            SequenceNumber = nextSequence,
            Role = DomainChatMessage.UserRole,
            Content = latestUserText,
            CreatedAt = now
        });
        context.ChatMessages.Add(new DomainChatMessage
        {
            ConversationId = conversation.Id,
            SequenceNumber = nextSequence + 1,
            Role = DomainChatMessage.AssistantRole,
            Content = reply.Text,
            CreatedAt = now
        });
        conversation.Language = request.Language ?? conversation.Language;
        conversation.LastActivityAt = now;
        conversation.LastAssistantMessageAt = now;
        conversation.InactivityDeadlineAt = now.AddMinutes(30);
        await context.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            reply = reply.Text,
            suggestedQuestions = reply.SuggestedQuestions ?? [],
            actions = reply.Actions ?? [],
            conversationId = conversation.Id,
            status = conversation.Status
        });
    }

    private static async Task<IResult> GetConversation(
        Guid id,
        IApplicationDbContext context,
        IUserContext userContext,
        string? clientSessionId,
        CancellationToken ct)
    {
        var conversation = await context.ChatConversations
            .AsNoTracking()
            .Include(x => x.Messages.OrderBy(m => m.SequenceNumber))
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (conversation is null || !OwnsConversation(conversation, userContext.UserId, clientSessionId))
        {
            return Results.NotFound(new { error = "Khong tim thay hoi thoai." });
        }

        return Results.Ok(new
        {
            conversationId = conversation.Id,
            status = conversation.Status,
            language = conversation.Language,
            messages = conversation.Messages.Select(x => new
            {
                id = x.Id,
                role = x.Role,
                text = x.Content,
                createdAt = x.CreatedAt,
                isAutoCloseMessage = x.IsAutoCloseMessage
            })
        });
    }

    private static async Task<IResult> CloseConversation(
        Guid id,
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        string? clientSessionId,
        CancellationToken ct)
    {
        var conversation = await context.ChatConversations.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (conversation is null || !OwnsConversation(conversation, userContext.UserId, clientSessionId))
        {
            return Results.NotFound(new { error = "Khong tim thay hoi thoai." });
        }

        if (conversation.Status == ChatConversation.OpenStatus)
        {
            var now = timeProvider.GetUtcNow();
            conversation.Status = ChatConversation.ClosedStatus;
            conversation.ClosedAt = now;
            conversation.CloseReason = "UserClosed";
            conversation.RetentionExpiresAt = now.AddDays(7);
            await context.SaveChangesAsync(ct);
        }

        return Results.Ok(new { conversationId = conversation.Id, status = conversation.Status });
    }

    private static bool OwnsConversation(ChatConversation conversation, Guid? userId, string? clientSessionId) =>
        (userId is not null && conversation.UserId == userId)
        || (userId is null && conversation.UserId is null
            && (conversation.AnonymousSessionId is null
                || (!string.IsNullOrWhiteSpace(clientSessionId)
                    && string.Equals(conversation.AnonymousSessionId, NormalizeSession(clientSessionId), StringComparison.Ordinal))));

    private static string? NormalizeSession(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <param name="BookingDraft">
    /// Trạng thái form đặt vé nhúng trong chat, do FE gửi lên khi form đang mở. BE nhận nguyên
    /// JSON, KHÔNG suy diễn cấu trúc và KHÔNG lưu lại — chỉ trích vài trường an toàn để đưa vào
    /// prompt (xem <see cref="AssistantBookingDraftSummary"/>) rồi bỏ. Bỏ trống hoặc gửi null
    /// nghĩa là lượt này không kèm form.
    /// </param>
    public sealed record ChatRequest(
        List<ChatTurnRequest>? Messages,
        string? Language = null,
        Guid? ConversationId = null,
        string? ClientSessionId = null,
        JsonElement? BookingDraft = null);

    public sealed record ChatTurnRequest(string? Role, string? Text);

    /// <summary>
    /// Chuẩn hoá draft từ client thành chuỗi JSON để chuyển xuống tầng Application. Trả null khi
    /// client không gửi gì hoặc gửi JSON null — hai trường hợp đó mang nghĩa "không có draft".
    /// </summary>
    private static string? NormalizeBookingDraft(JsonElement? draft)
    {
        if (draft is not JsonElement element
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return element.GetRawText();
    }
}
