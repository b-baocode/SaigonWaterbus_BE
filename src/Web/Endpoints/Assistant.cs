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
    private const int MaxBookingDraftJsonLength = 32 * 1024;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(Chat, "chat")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Chat voi tro ly ao Saigon Waterbus");

        group.MapGet(GetConversation, "conversations/{id:guid}").AllowAnonymous();
        group.MapPut(UpdateBookingDraft, "conversations/{id:guid}/booking-draft")
            .AllowAnonymous()
            .WithSummary("Cap nhat trang thai nhap dat ve trong hoi thoai");
        group.MapPost(CloseConversation, "conversations/{id:guid}/close").AllowAnonymous();
    }

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

        var bookingDraftResult = NormalizeBookingDraft(request.BookingDraft);
        if (bookingDraftResult.Error is not null)
        {
            return Results.BadRequest(new { error = bookingDraftResult.Error });
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
                InactivityDeadlineAt = now.AddMinutes(30),
                BookingDraftJson = bookingDraftResult.Json
            };
            context.ChatConversations.Add(conversation);
        }

        if (bookingDraftResult.HasValue)
        {
            conversation.BookingDraftJson = bookingDraftResult.Json;
        }

        var history = conversation.Messages
            .Where(x => !x.IsAutoCloseMessage && (x.Role == DomainChatMessage.UserRole || x.Role == DomainChatMessage.AssistantRole))
            .OrderBy(x => x.SequenceNumber)
            .Select(x => new AssistantTurn(x.Role, x.Content))
            .Append(new AssistantTurn(DomainChatMessage.UserRole, latestUserText))
            .ToArray();

        var reply = await sender.Send(
            new ChatWithAssistantCommand(
                history,
                request.Language ?? conversation.Language,
                conversation.BookingDraftJson),
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
            status = conversation.Status,
            bookingDraft = ParseBookingDraft(conversation.BookingDraftJson)
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
            bookingDraft = ParseBookingDraft(conversation.BookingDraftJson),
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

    private static async Task<IResult> UpdateBookingDraft(
        Guid id,
        IApplicationDbContext context,
        IUserContext userContext,
        BookingDraftRequest request,
        CancellationToken ct)
    {
        if (request.BookingDraft.ValueKind == JsonValueKind.Undefined)
        {
            return Results.BadRequest(new { error = "bookingDraft la bat buoc." });
        }

        var normalized = NormalizeBookingDraft(request.BookingDraft);
        if (normalized.Error is not null)
        {
            return Results.BadRequest(new { error = normalized.Error });
        }

        var conversation = await context.ChatConversations
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (conversation is null || !OwnsConversation(conversation, userContext.UserId, request.ClientSessionId))
        {
            return Results.NotFound(new { error = "Khong tim thay hoi thoai." });
        }

        if (conversation.Status != ChatConversation.OpenStatus)
        {
            return Results.Conflict(new { error = "Hoi thoai da dong. Vui long bat dau hoi thoai moi." });
        }

        conversation.BookingDraftJson = normalized.Json;
        await context.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            conversationId = conversation.Id,
            status = conversation.Status,
            bookingDraft = ParseBookingDraft(conversation.BookingDraftJson)
        });
    }

    private static bool OwnsConversation(ChatConversation conversation, Guid? userId, string? clientSessionId) =>
        (userId is not null && conversation.UserId == userId)
        || (userId is null && conversation.UserId is null
            && (conversation.AnonymousSessionId is null
                || (!string.IsNullOrWhiteSpace(clientSessionId)
                    && string.Equals(conversation.AnonymousSessionId, NormalizeSession(clientSessionId), StringComparison.Ordinal))));

    private static string? NormalizeSession(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (bool HasValue, string? Json, string? Error) NormalizeBookingDraft(JsonElement draft)
    {
        if (draft.ValueKind == JsonValueKind.Undefined)
        {
            return (false, null, null);
        }

        if (draft.ValueKind == JsonValueKind.Null)
        {
            return (true, null, null);
        }

        if (draft.ValueKind != JsonValueKind.Object)
        {
            return (true, null, "bookingDraft phai la mot JSON object hoac null.");
        }

        var json = draft.GetRawText();
        if (json.Length > MaxBookingDraftJsonLength)
        {
            return (true, null, $"bookingDraft vuot qua gioi han {MaxBookingDraftJsonLength} ky tu.");
        }

        return (true, json, null);
    }

    private static JsonElement? ParseBookingDraft(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public sealed record ChatRequest(
        List<ChatTurnRequest>? Messages,
        string? Language = null,
        Guid? ConversationId = null,
        string? ClientSessionId = null,
        JsonElement BookingDraft = default);

    public sealed record BookingDraftRequest(
        JsonElement BookingDraft,
        string? ClientSessionId = null);

    public sealed record ChatTurnRequest(string? Role, string? Text);
}
