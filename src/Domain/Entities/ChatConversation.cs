namespace SaigonWaterbus.Domain.Entities;

public class ChatConversation
{
    public const string OpenStatus = "Open";
    public const string ClosedStatus = "Closed";

    /// <summary>
    /// Không ép ngôn ngữ: trợ lý tự bám theo ngôn ngữ khách đang viết. Đây là giá trị của mọi hội
    /// thoại mới kể từ khi bỏ trường <c>language</c> khỏi request chat. Các hàng cũ vẫn giữ
    /// "VN"/"ENG" và vẫn được tôn trọng, nên hội thoại cũ không đổi hành vi giữa chừng.
    /// </summary>
    public const string AutoLanguage = "Auto";

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string? AnonymousSessionId { get; set; }
    public string Status { get; set; } = OpenStatus;
    public string Language { get; set; } = "VN";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset LastAssistantMessageAt { get; set; }
    public DateTimeOffset InactivityDeadlineAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? CloseReason { get; set; }
    public DateTimeOffset? RetentionExpiresAt { get; set; }
    public DateTimeOffset? AutoCloseMessageSentAt { get; set; }

    public User? User { get; set; }
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
