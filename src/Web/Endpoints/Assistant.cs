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
          "messages": [{ "text": "chon giup minh 2 ghe canh nhau" }],
          "conversationId": "0f8b1d42-5c6a-4b7e-9d10-2a3b4c5d6e7f",
          "clientSessionId": "web-8f1c...",
          "bookingDraft": {
            "serviceType": "Waterbus",
            "departureDate": "2026-08-20",
            "isRoundTrip": false,
            "fromStationCode": "ST-BD", "fromStationName": "Ben Bach Dang",
            "toStationCode": "ST-TT",   "toStationName": "Ben Thu Thiem",
            "selectedDepartureTrip": {
              "tripId": "6d4e2f10-1a2b-4c3d-8e9f-0a1b2c3d4e5f",
              "tripCode": "TR-20260820-R5-BD-TT-4858"
            },
            "selectedSeatsDeparture": []
          }
        }
        """;

    private const string BookingDraftNote =
        """
        bookingDraft — thong tin dat ve tro ly dang gom giup khach.

        CACH DUNG, 2 dong:
          const res = await postChat({ messages, conversationId, clientSessionId, bookingDraft: draft });
          draft = res.bookingDraft;      // copy NGUYEN KHOI, luot sau gui lai chinh no
        Server merge san — client khong phai tu merge, khong phai doi ten field.
        Luot dau bo han bookingDraft cung duoc. Mo chat TU trang dat ve thi seed nhung gi trang dang co.

        BE CHI DOC cac truong sau. Truong khac (passengers, contact, preview...) van duoc giu nguyen
        va tra lai y nguyen nhung BE bo qua — dung gui cho nang request (tran 64KB, vuot thi 400):
          serviceType             Waterbus | Sightseeing. Bo trong = Waterbus.
                                  Tour ngam canh PHAI gui "Sightseeing", khong thi tro ly hoi ben
                                  di/ben den cho mot tuyen chay vong.
          departureDate           yyyy-MM-dd (sai dinh dang = coi nhu chua co)
          isRoundTrip             true | false
          returnDate              yyyy-MM-dd, chi doc khi isRoundTrip = true
          fromStationCode         ma ben di (thieu thi BE tu tra tu ten ben)
          toStationCode           ma ben den
          fromStationName         ten ben di (cat con 60 ky tu)
          toStationName           ten ben den
          selectedDepartureTrip   chuyen DI. PHAI co tripId neu muon tro ly chon ghe ho.
          selectedReturnTrip      chuyen VE, cung quy tac (chi doc khi khu hoi)
          selectedSeatsDeparture  CHI dem so phan tu
          selectedSeatsReturn     CHI dem so phan tu (chi doc khi khu hoi)
        adultCount/childCount/infantCount KHONG con duoc doc — so ghe lay tu cau khach noi.

        RESPONSE co 2 truong ve draft:
          bookingDraft   draft DAY DU da merge — dung cai nay de giu trang thai.
                         null chi khi ban khong gui draft va luot nay cung khong ghi nhan gi.
          draftPatch     rieng phan vua doi, thuong null. Chi can neu muon to sang cho AI vua dien.
                         Tu merge thi: field khac null GHI DE, field null GIU NGUYEN. DUNG dung
                         Object.assign(draft, patch) — cac null se xoa sach thong tin khach da cho.

        Bon khoa trong draftPatch doi ten khi vao draft:
          trip       -> selectedDepartureTrip      seats       -> selectedSeatsDeparture
          returnTrip -> selectedReturnTrip         returnSeats -> selectedSeatsReturn

        Ten field ben trong trip/seats trung TripSummaryDto va TripSeatMapSeatDto (tu /api/trips/search
        va /api/trips/{id}/seats) nen gan thang sang state trang dat ve:
          fromStationId/toStationId  Guid ben that — cho o chon ben va API tim chuyen
          totalSeats                 hien "con trong x/y"
          boatId                     tai anh tau
          routeName                  tour ngam canh tach ten ben tu day
          row/column                 vi tri ghe tren luoi so do ghe

        HAI CAI BAY:
          1. DUNG XOA DRAFT khi khach go chu — "chon giup minh 2 ghe" cung la go chu, xoa luc do la
             mat sach thu tro ly vua ghi nhan.
          2. Server chi GHI DE, KHONG xoa field. Khach doi ngay hoac doi chang sau khi da chon chuyen
             + ghe thi draft van con chuyen/ghe cu — client tu don khi thay departureDate,
             fromStationCode, toStationCode hoac serviceType thay doi.

        GHE CHUA DUOC GIU CHO: tro ly chi chon tam, khach van phai bam giu ghe o trang dat ve.
        Moi gia tri (ten ben, ngay, ma chuyen, ghe) deu duoc server kiem truoc khi phat ra.

        actions[] — nut "open-booking": xuat hien o luot tro ly VUA ghi nhan them thong tin va da du
        de khach di tiep. Khac action "navigate" o cho co them "step":
          { "type": "open-booking", "label": "Chon ghe", "route": "/waterbus-booking", "step": 2 }
          step 1  du ngay + chang                          -> mo o buoc tim chuyen
          step 2  da co chuyen (khu hoi: du ca hai chieu)   -> mo thang buoc chon ghe
          route   tu doi sang /watersightseeing-booking khi la tour ngam canh
        Khong co step 3: buoc thanh toan doi ghe DA GIU, ma tro ly co y khong giu cho.
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
                "MOI LUOT gui: messages + conversationId + clientSessionId + bookingDraft. Luot dau "
                + "conversationId = null, server tao roi tra ve; tu luot 2 gui lai dung Guid do.",
                "messages CHI can cau MOI NHAT cua khach (BE lay phan tu cuoi cung co chu). Lich su "
                + "server tu doc tu DB — dung gui lai ca hoi thoai, va DUNG gui luot cua tro ly vao "
                + "day, no se bi hieu nham thanh cau hoi cua khach.",
                "clientSessionId: BAT BUOC voi khach chua dang nhap, doi giua chung se bi 404. Khach "
                + "da dang nhap thi so khop theo userId, truong nay bi bo qua.",
                "NGON NGU tu dong theo cau khach viet, client khong phai khai bao. Rieng "
                + "suggestedQuestions va nhan trong actions[] LUON tieng Viet (chon theo tu khoa, "
                + "khong do LLM sinh).",
                "Hai truong DA BO nhung gui kem van chay (chi bi bo qua): \"role\" trong messages va "
                + "\"language\" o cap goc.",
                BookingDraftNote,
                "bookingDraft KHONG luu tren server — client PHAI tu giu (localStorage) va gui kem MOI "
                + "luot. Lich su hoi thoai khong thay the duoc draft: khach F5 ma mat draft la tro ly "
                + "hoi lai tu dau. Noi dung trong draft do client kiem soat nen BE lam sach tung gia "
                + "tri truoc khi dua vao prompt — dung trong cho no de dieu khien tro ly.",
                "RESPONSE: reply | bookingDraft | draftPatch | actions[] | suggestedQuestions[] | "
                + "conversationId | status (Open|Closed).",
                "MA LOI: 400 = messages rong hoac draft qua 64KB. 404 = khong thay hoi thoai hoac sai "
                + "clientSessionId. 409 = hoi thoai da dong (mo moi voi conversationId = null). "
                + "429 = qua 8 luot/60s. Hoi thoai tu dong dong sau 30 phut khong hoat dong."))
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
        // Client chỉ gửi text, KHÔNG còn gửi role: lượt assistant vốn đã bị bỏ (lịch sử đọc từ DB
        // theo conversationId), nên role không mang thêm thông tin gì. Lấy phần tử cuối cùng có
        // chữ — client cũ còn gửi kèm "role" thì System.Text.Json tự bỏ qua, không vỡ.
        var latestUserText = (request.Messages ?? [])
            .LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Text))
            ?.Text?.Trim();
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
                Language = ChatConversation.AutoLanguage,
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

        // Ngôn ngữ lấy từ hội thoại, không nhận từ request nữa. Hội thoại mới mang "Auto" nên
        // AssistantLanguage.Resolve trả null = trợ lý tự bám ngôn ngữ khách viết; hội thoại cũ còn
        // "VN"/"ENG" thì vẫn giữ nguyên hành vi đã có, không đổi giữa chừng.
        //
        // Ngoại lệ: tin nhắn mồi "start"/"bắt đầu" lúc client mở khung chat quyết định ngôn ngữ của
        // riêng LỜI CHÀO. Cố ý KHÔNG ghi vào conversation.Language — từ lượt sau khách gõ tiếng gì
        // thì trả lời tiếng đó.
        var language = request.ConversationId is null
            ? AssistantLanguage.ResolveGreetingTrigger(latestUserText) ?? conversation.Language
            : conversation.Language;

        var reply = await sender.Send(
            new ChatWithAssistantCommand(history, language, incomingDraft),
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
        conversation.LastActivityAt = now;
        conversation.LastAssistantMessageAt = now;
        conversation.InactivityDeadlineAt = now.AddMinutes(30);
        await context.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            reply = reply.Text,
            suggestedQuestions = reply.SuggestedQuestions ?? [],
            actions = reply.Actions ?? [],
            bookingDraft = reply.BookingDraft,
            draftPatch = reply.DraftPatch,
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
        Guid? ConversationId = null,
        string? ClientSessionId = null,
        JsonElement? BookingDraft = null);

    public sealed record ChatTurnRequest(string? Text);

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
