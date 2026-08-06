using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data;

/// <summary>Đóng hội thoại sau 30 phút không hoạt động và xóa dữ liệu sau 7 ngày.</summary>
public sealed class ChatConversationLifecycleService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(7);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChatConversationLifecycleService> _logger;

    public ChatConversationLifecycleService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ChatConversationLifecycleService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chat conversation lifecycle processing failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var stale = await db.ChatConversations
            .Where(x => x.Status == ChatConversation.OpenStatus && x.InactivityDeadlineAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var conversation in stale)
        {
            conversation.Status = ChatConversation.ClosedStatus;
            conversation.ClosedAt = now;
            conversation.CloseReason = "Timeout";
            conversation.RetentionExpiresAt = now.Add(RetentionWindow);
            conversation.AutoCloseMessageSentAt = now;
            var nextSequence = await db.ChatMessages
                .Where(x => x.ConversationId == conversation.Id)
                .Select(x => (int?)x.SequenceNumber).MaxAsync(cancellationToken) ?? 0;
            db.ChatMessages.Add(new ChatMessage
            {
                ConversationId = conversation.Id,
                SequenceNumber = nextSequence + 1,
                Role = ChatMessage.AssistantRole,
                Content = "Mình chưa nhận được phản hồi thêm từ bạn trong 30 phút. Hội thoại đã được đóng. Bạn có thể bắt đầu cuộc trò chuyện mới khi cần hỗ trợ.",
                CreatedAt = now,
                IsAutoCloseMessage = true
            });
        }

        var expired = await db.ChatConversations
            .Where(x => x.Status == ChatConversation.ClosedStatus
                && x.RetentionExpiresAt != null
                && x.RetentionExpiresAt <= now)
            .ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            db.ChatConversations.RemoveRange(expired);
        }

        if (stale.Count > 0 || expired.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
