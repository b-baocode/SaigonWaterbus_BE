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

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(Chat, "chat")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Chat voi tro ly ao Saigon Waterbus");

        group.MapGet(GetConversation, "conversations/{id:guid}").AllowAnonymous();
        group.MapPost(CloseConversation, "conversations/{id:guid}/close").AllowAnonymous();
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

        // Draft do FE gửi kèm khi form đặt vé trong chat đang mở. CỐ Ý KHÔNG LƯU: nó chỉ cần
        // sống trong đúng lượt này để trợ lý biết khách đang ở bước nào mà không hỏi lại.
        // Việc giữ draft qua F5 / đổi tab là của FE (localStorage) — nhờ vậy không cần cột DB,
        // không cần migration.
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
