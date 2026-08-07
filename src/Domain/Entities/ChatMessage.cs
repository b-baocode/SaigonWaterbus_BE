namespace SaigonWaterbus.Domain.Entities;

public class ChatMessage
{
    public const string UserRole = "user";
    public const string AssistantRole = "assistant";
    public const string SystemRole = "system";
    public const string ToolRole = "tool";

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public int SequenceNumber { get; set; }
    public string Role { get; set; } = UserRole;
    public string Content { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsAutoCloseMessage { get; set; }

    public ChatConversation Conversation { get; set; } = null!;
}
