using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("message_id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(x => x.SequenceNumber).HasColumnName("sequence_number").IsRequired();
        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Content).HasColumnName("content").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.IsAutoCloseMessage).HasColumnName("is_auto_close_message");
        builder.HasIndex(x => new { x.ConversationId, x.SequenceNumber }).IsUnique();
        builder.HasOne(x => x.Conversation).WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}
