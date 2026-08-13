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
            "stage": "SelectingSeats",
            "serviceType": "Waterbus",
            "fromStationName": "Bạch Đằng",
            "toStationName": "Thủ Thiêm",
            "fromStationCode": "BD",
            "toStationCode": "TT",
            "departureDate": "2026-08-20",
            "isRoundTrip": false,
            "returnDate": null,
            "adultCount": 2,
            "childCount": 0,
            "infantCount": 0,
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
        bookingDraft (optional): trang thai form dat ve dang mo trong khung chat. LA MOT OBJECT
        JSON, khong phai chuoi — Swagger hien "string" chi vi khong sinh duoc schema cho kieu nay.

        BE chi doc dung cac truong duoi day, moi truong con lai (departureTrips, passengers,
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
          fromStationCode         : ma ga di (dung cho tool chon ghe; thieu thi BE tu tra tu ten ga)
          toStationCode           : ma ga den
          selectedDepartureTrip   : object chuyen da chon. Prompt chi biet "da chon hay chua";
                                    rieng tool chon ghe doc them tripId (Guid) va tripCode.
                                    Gui thieu tripId thi tro ly khong chon ghe ho duoc.
          selectedSeatsDeparture  : CHI dem so phan tu

        RESPONSE tra ve bookingDraft ĐẦY ĐỦ (draft ban vua gui, da duoc server ap thay doi cua luot
        nay). CACH DUNG DON GIAN NHAT — va la cach duoc khuyen dung:

          draft = response.bookingDraft;   // copy nguyen khoi
          // luot sau gui lai y nguyen: { messages: [...], conversationId, clientSessionId, bookingDraft: draft }

        KHONG phai tu merge, KHONG phai doi ten field, KHONG phai doan gi tu cau tra loi text.
        bookingDraft = null chi khi ban khong gui draft VA luot nay tro ly cung khong dien gi.
        Moi field LA cua ban (stage, passengers, contact, preview...) duoc GIU NGUYEN, server chi
        ghi de dung nhung khoa no vua doi.

        RESPONSE con co draftPatch: RIENG phan tro ly vua doi trong luot nay (null = khong doi gi).
        Chi dung khi ban muon highlight "AI vua dien o day" tren form; con de giu trang thai thi
        dung bookingDraft o tren.

          "draftPatch": {
            "serviceType": "Waterbus",            // hoac "Sightseeing"
            "departureDate": "2026-08-14", "returnDate": null, "isRoundTrip": false,
            "fromStationCode": "ST-BD", "fromStationName": "Ben Bach Dang",
            "toStationCode": "ST-LD",   "toStationName": "Ben Linh Dong",
            "adultCount": 2, "childCount": 0, "infantCount": 0,
            "trip": {                             // chi co khi tro ly chon CHUYEN ho khach
              "tripId": "a7b580cb-...", "tripCode": "BB-...",
              "fromStopScheduledDeparture": "2026-08-14T01:00:00+00:00",
              "toStopScheduledArrival": "2026-08-14T02:52:00+00:00",
              "minPrice": 19000, "availableSeats": 79
            },
            "tripCode": "BB-...",                 // chi co khi tro ly chon GHE
            "seats": [ { "seatNumber": "1-A1", "deck": 1, "price": 19000, "seatTypeName": "Standard" } ]
          }

        NEU van muon tu merge draftPatch (khong bat buoc — server da merge san o bookingDraft):
        field khac null thi GHI DE, field null thi GIU NGUYEN gia tri dang co.
          Object.entries(patch).forEach(([k, v]) => { if (v !== null) draft[k] = v; });
        DUNG dung Object.assign(draft, patch): patch luon co DU field nen cac null se xoa sach thu
        khach da dien. Rieng hai truong doi ten: "seats" ghi vao selectedSeatsDeparture, "trip" ghi
        vao selectedDepartureTrip. Ten field ben trong "trip" CO Y trung voi TripSummaryDto ma
        client nhan tu /api/trips/search, nen gan thang duoc, khong phai anh xa lai.

        MOI GIA TRI DA DUOC SERVER KIEM truoc khi phat ra: ten ben phai khop ben co that (tra ve ca
        ma lan ten chuan), ngay dung yyyy-MM-dd va khong o qua khu, tong khach toi da 10 va it nhat
        1 nguoi lon, tour ngam canh thi KHONG bao gio co ben di/ben den. Chuyen phai nam trong ket
        qua tim chuyen cua DUNG ngay + chang do va con nhan dat cho — tro ly khong the dien mot
        chuyen ngay khac hay tuyen khac. Ghe da kiem con trong tren so do ghe that NHUNG chua giu
        cho — khach van phai bam nut giu ghe.

        Dieu kien de tro ly chon duoc ghe: da co chuyen (tu selectedDepartureTrip cua draft, hoac
        do chinh luot chat nay vua chon) va co so khach. Thieu thi tro ly hoi khach truoc.

        GIOI HAN da biet: chi chon duoc ghe CHIEU DI. Khach khu hoi dang o buoc chon ghe chieu ve
        ma nho tro ly chon ho thi ghe CHIEU DI se bi ghi de (selectedReturnTrip chua duoc doc) —
        chua ho tro, dung dua vao.

        Luot chat co draftPatch KHONG duoc coi la "khach sua thong tin": client phai GIU form dang
        mo va nhan lai bookingDraft tra ve. Neu client xoa draft moi khi khach go chu thi thu vua
        dien se mat ngay.

        SERVER KHONG XOA FIELD: patch chi GHI DE, khong co cach bao "bo chuyen nay di". Nen khi
        khach doi ngay/doi chang sau luc da chon chuyen + ghe, bookingDraft tra ve VAN con
        selectedDepartureTrip va selectedSeatsDeparture cua ngay cu — client tu don hai khoa do khi
        thay departureDate/fromStationCode/toStationCode/serviceType thay doi.
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
                "messages: moi phan tu CHI co truong text. BE lay PHAN TU CUOI CUNG co chu lam cau "
                + "hoi cua khach; lich su truoc do server tu doc lai tu DB theo conversationId, client "
                + "khong can gui lai. Luot dau tien de conversationId = null, server tao roi tra ve.",
                "Truong \"role\" DA BO (truoc day nhan \"user\"/\"assistant\"). Client cu con gui kem "
                + "role thi van chay binh thuong — truong la bi bo qua. Nhung DUNG gui ca luot assistant "
                + "nua: BE khong con loc theo role, gui vao ma luot cuoi la cau cua tro ly thi no se bi "
                + "hieu nham thanh cau hoi cua khach.",
                "Truong \"language\" DA BO. Tro ly TU BAM theo ngon ngu khach dang viet: khach nhan "
                + "tieng Anh thi tra loi tieng Anh, tieng Viet thi tra loi tieng Viet, khong can client "
                + "khai bao. Gui kem language cung khong sao, truong la bi bo qua.",
                "HE QUA can biet: suggestedQuestions va nhan trong actions[] luon la TIENG VIET, vi "
                + "chung duoc chon theo tu khoa chu khong do LLM sinh. Rieng cau tra loi (reply) van "
                + "dung ngon ngu cua khach.",
                "Hoi thoai tao truoc thay doi nay con luu language \"VN\"/\"ENG\" thi VAN bi ep ngon ngu "
                + "do cho toi khi khach bat dau hoi thoai moi — co y, de hoi thoai dang do khong doi "
                + "giong giua chung.",
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
                + "bookingDraft (DRAFT DAY DU da merge san — copy nguyen khoi vao request luot sau), "
                + "draftPatch (rieng phan tro ly vua doi, thuong null), conversationId, "
                + "status (Open|Closed).",
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
        var reply = await sender.Send(
            new ChatWithAssistantCommand(history, conversation.Language, incomingDraft),
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
