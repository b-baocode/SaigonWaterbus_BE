namespace SaigonWaterbus.Domain.Entities;

public class ChatConversation
{
    public const string OpenStatus = "Open";
    public const string ClosedStatus = "Closed";

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

    /// <summary>
    /// Structured booking state collected during the current chat flow.
    /// The full conversation remains in ChatMessages; this JSON is only the
    /// server-side draft used to resume and validate the booking flow.
    /// </summary>
    public string? BookingDraftJson { get; set; }

    public User? User { get; set; }
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
