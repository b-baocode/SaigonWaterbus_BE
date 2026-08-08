using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("chat_conversations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("conversation_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.AnonymousSessionId).HasColumnName("anonymous_session_id").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Language).HasColumnName("language").HasMaxLength(10).IsRequired();
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.LastActivityAt).HasColumnName("last_activity_at");
        builder.Property(x => x.LastAssistantMessageAt).HasColumnName("last_assistant_message_at");
        builder.Property(x => x.InactivityDeadlineAt).HasColumnName("inactivity_deadline_at");
        builder.Property(x => x.ClosedAt).HasColumnName("closed_at");
        builder.Property(x => x.CloseReason).HasColumnName("close_reason").HasMaxLength(30);
        builder.Property(x => x.RetentionExpiresAt).HasColumnName("retention_expires_at");
        builder.Property(x => x.AutoCloseMessageSentAt).HasColumnName("auto_close_message_sent_at");
        builder.Property(x => x.BookingDraftJson)
            .HasColumnName("booking_draft_json")
            .HasColumnType("jsonb")
            .IsRequired(false);
        builder.HasIndex(x => new { x.Status, x.InactivityDeadlineAt });
        builder.HasIndex(x => x.RetentionExpiresAt);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.AnonymousSessionId);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
