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
        ============ bookingDraft: thong tin dat ve tro ly dang giup khach gom ============

        1) CACH DUNG — chi can nho 2 dong nay:

             const res = await postChat({ messages, conversationId, clientSessionId, bookingDraft: draft });
             draft = res.bookingDraft;   // copy NGUYEN KHOI, khong sua gi, luot sau gui lai chinh no

           Server da merge san thay doi cua luot nay vao res.bookingDraft. Client KHONG phai tu
           merge, KHONG phai doi ten field, KHONG phai doan gi tu cau tra loi text.

        2) LUOT DAU TIEN gui gi? Khong gui gi cung duoc — bo han bookingDraft, tro ly van tra ve
           draft day du de ban dung tiep. Neu mo chat TU trang dat ve thi seed nhung gi trang dang
           co (bang duoi) de tro ly khoi hoi lai.

        3) BE CHI DOC dung cac truong sau. Truong khac (departureTrips, passengers, contact,
           preview, promotionCode...) van duoc giu nguyen va tra lai y nguyen, nhung BE khong doc —
           dung gui cho nang request (tran 64KB, vuot thi 400):

             serviceType             Waterbus | Sightseeing. Bo trong = Waterbus.
                                     Tour ngam canh PHAI gui "Sightseeing", khong thi tro ly di hoi
                                     ben di/ben den cho mot tuyen chay vong.
             departureDate           yyyy-MM-dd (sai dinh dang = coi nhu chua co)
             isRoundTrip             true | false
             returnDate              yyyy-MM-dd, chi doc khi isRoundTrip = true
             fromStationCode         ma ben di. Thieu thi BE tu tra tu ten ben.
             toStationCode           ma ben den
             fromStationName         ten ben di (cat con 60 ky tu)
             toStationName           ten ben den
             selectedDepartureTrip   chuyen DI da chon. PHAI co tripId (Guid) neu muon tro ly chon
                                     ghe ho; field khac trong object nay BE khong doc.
             selectedReturnTrip      chuyen VE, cung quy tac (chi doc khi khu hoi)
             selectedSeatsDeparture  CHI dem so phan tu
             selectedSeatsReturn     CHI dem so phan tu (chi doc khi khu hoi)

           adultCount/childCount/infantCount KHONG con duoc doc: tro ly khong hoi so khach nua, so
           ghe lay tu chinh cau khach noi ("cho minh 2 ghe"). Gui kem cung khong sao.

        ============ Response tra ve gi ============

        bookingDraft  Draft DAY DU sau khi ap thay doi cua luot nay — dung cai nay de giu trang thai.
                      null chi khi ban khong gui draft VA luot nay tro ly cung khong ghi nhan gi.

        draftPatch    RIENG phan tro ly vua doi trong luot nay (null = khong doi gi). Chi can khi
                      ban muon to sang "AI vua dien o day" tren giao dien:

                        {
                          "serviceType": "Waterbus",
                          "departureDate": "2026-08-14", "returnDate": null, "isRoundTrip": false,
                          "fromStationId": "87714aa9-...", "fromStationCode": "ST-BD", "fromStationName": "Ben Bach Dang",
                          "toStationId": "779d7b32-...",  "toStationCode": "ST-LD",   "toStationName": "Ben Linh Dong",
                          "trip": {
                            "tripId": "a7b580cb-...", "tripCode": "BB-...", "routeName": "Bach Dang - Linh Dong",
                            "fromStopScheduledDeparture": "2026-08-14T01:00:00+00:00",
                            "toStopScheduledArrival": "2026-08-14T02:52:00+00:00",
                            "minPrice": 19000, "availableSeats": 79, "totalSeats": 80,
                            "boatId": "3f2c1b90-..."
                          },
                          "returnTrip":  { /* cung shape, chuyen chieu ve */ },
                          "seats":       [ { "seatNumber": "1-A1", "deck": 1, "row": "A", "column": 1,
                                             "price": 19000, "seatTypeName": "Standard" } ],
                          "returnSeats": [ /* cung shape, ghe chieu ve */ ]
                        }

                      Bon khoa doi ten khi vao draft:
                        trip       -> selectedDepartureTrip     seats       -> selectedSeatsDeparture
                        returnTrip -> selectedReturnTrip        returnSeats -> selectedSeatsReturn

                      Neu tu merge (khong bat buoc): field khac null thi GHI DE, field null thi GIU
                      NGUYEN gia tri cu. DUNG dung Object.assign(draft, patch) — patch luon co du
                      field nen cac null se xoa sach thong tin khach da cho:
                        Object.entries(patch).forEach(([k, v]) => { if (v !== null) draft[k] = v; });

        Ten field trong "trip"/"seats" CO Y trung voi TripSummaryDto va TripSeatMapSeatDto ma client
        nhan tu /api/trips/search va /api/trips/{id}/seats, nen gan thang sang state trang dat ve:
          fromStationId/toStationId  Guid ben that — dung cho o chon ben va API tim chuyen
          totalSeats                 de hien "con trong x/y"
          boatId                     de tai anh tau
          routeName                  tuyen ngam canh tach ten ben tu day (tuyen vong, khong co chang)
          row/column                 vi tri ghe tren luoi so do ghe

        ============ Server dam bao gi ============

        Moi gia tri deu duoc kiem TRUOC khi phat ra, client khong phai kiem lai:
          - ten ben khop ben co that (tra ve ca id, ma va ten chuan)
          - ngay dung yyyy-MM-dd va khong nam trong qua khu
          - chuyen nam trong ket qua tim chuyen cua DUNG ngay + chang do va con nhan dat cho; chuyen
            VE tim theo ngay ve va chang DAO NGUOC — tro ly khong the ghi mot chuyen ngay khac hay
            tuyen khac
          - toi da 10 ghe moi lan chon, ghe da kiem con trong tren so do ghe that
          - tour ngam canh KHONG bao gio co ben di/ben den va KHONG co chuyen ve

        GHE CHUA DUOC GIU CHO: tro ly chi chon tam. Khach van phai bam giu ghe o trang dat ve, luc
        do he thong moi kiem lan cuoi — dung hien "da giu ghe" cho khach.

        Tro ly chon duoc ghe cua mot chieu khi chieu do da co chuyen (tu draft, hoac do chinh luot
        chat nay vua chon) va khach da noi can may ghe. Thieu thi tro ly hoi truoc.

        ============ Hai cai bay hay dinh ============

        1. DUNG XOA DRAFT khi khach go chu. "Chon giup minh 2 ghe" cung la go chu — xoa draft luc do
           la mat sach thu tro ly vua ghi nhan. Luot co draftPatch KHONG phai "khach sua thong tin"
           ma la "tro ly vua dien them".

        2. SERVER KHONG XOA FIELD: patch chi ghi de, khong co cach bao "bo chuyen nay di". Nen khi
           khach doi ngay hoac doi chang sau luc da chon chuyen + ghe, bookingDraft tra ve VAN con
           selectedDepartureTrip/selectedSeatsDeparture cu — client tu don cac khoa do khi thay
           departureDate/fromStationCode/toStationCode/serviceType thay doi.

        ============ actions[]: nut "open-booking" ============

        Xuat hien o luot tro ly VUA ghi nhan them thong tin va da du de khach di tiep. Khac cac
        action "navigate" thong thuong o cho co them "step":

          { "type": "open-booking", "label": "Chon ghe", "route": "/waterbus-booking", "step": 2 }

          step 1  du ngay + chang            -> mo o buoc tim chuyen
          step 2  da co chuyen (khu hoi: du ca hai chieu) -> mo thang buoc chon ghe
          route   tu doi sang /watersightseeing-booking khi la tour ngam canh

        KHONG bao gio co step 3: buoc thanh toan doi ghe DA GIU, ma tro ly co y khong giu cho.
        Client dung bookingDraft dung state cho trang dat ve roi dieu huong kem step nay.
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
                "GUI GI MOI LUOT: messages (mang, moi phan tu chi co truong text) + conversationId + "
                + "clientSessionId + bookingDraft. Luot dau de conversationId = null, server tao roi "
                + "tra ve; tu luot 2 gui lai dung Guid do.",
                "messages CHI can cau MOI NHAT cua khach — BE lay phan tu cuoi cung co chu, con lich "
                + "su thi server tu doc tu DB theo conversationId. DUNG gui lai ca hoi thoai, va nhat "
                + "la dung gui luot cua tro ly: BE khong loc theo role nen luot cuoi la cau cua bot "
                + "thi no bi hieu nham thanh cau hoi cua khach.",
                "clientSessionId: BAT BUOC voi khach chua dang nhap — day la thu duy nhat chung minh "
                + "hoi thoai la cua minh, doi giua chung se bi 404. Khach da dang nhap thi so khop "
                + "theo userId, truong nay bi bo qua.",
                "NGON NGU tu dong: khach viet tieng Anh thi tra loi tieng Anh, tieng Viet thi tieng "
                + "Viet, client khong phai khai bao. Rieng suggestedQuestions va nhan trong actions[] "
                + "LUON tieng Viet vi chung chon theo tu khoa chu khong do LLM sinh.",
                "HAI TRUONG DA BO — client cu gui kem van chay, chi bi bo qua: \"role\" trong moi phan "
                + "tu messages, va \"language\" o cap goc. Hoi thoai tao truoc khi bo \"language\" van "
                + "bi ep ngon ngu da luu cho toi khi mo hoi thoai moi (co y, de hoi thoai dang do "
                + "khong doi giong giua chung).",
                BookingDraftNote,
                "bookingDraft KHONG duoc luu tren server, no chi song trong dung request do. Client "
                + "PHAI tu giu (localStorage/sessionStorage) va gui kem MOI luot — lich su hoi thoai "
                + "KHONG thay the duoc draft, khach F5 ma mat draft la tro ly hoi lai tu dau.",
                "Noi dung trong bookingDraft do client kiem soat nen BE coi la KHONG dang tin: tung "
                + "gia tri deu duoc lam sach roi moi nhet vao prompt, kem cau ra lenh bo qua moi chi "
                + "dan an ben trong. Dung trong cho no de dieu khien tro ly.",
                "RESPONSE: reply (text tra loi) | bookingDraft (draft day du da merge san — copy "
                + "nguyen khoi cho luot sau) | draftPatch (rieng phan vua doi, thuong null) | "
                + "actions[] (nut dieu huong, xem muc open-booking) | suggestedQuestions[] | "
                + "conversationId | status (Open|Closed).",
                "MA LOI: 400 = messages rong hoac bookingDraft qua 64KB. 404 = khong tim thay hoi "
                + "thoai, hoac sai clientSessionId. 409 = hoi thoai da dong, phai mo hoi thoai moi "
                + "(conversationId = null). 429 = qua 8 luot/60s (dem theo user, hoac X-Device-Id, "
                + "hoac IP).",
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
