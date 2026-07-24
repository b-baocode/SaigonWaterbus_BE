namespace SaigonWaterbus.Domain.Entities;

public class CharterRouteDrawRequest : BaseGuidAuditableEntity
{
    public const string PendingStatus = "Pending";
    public const string InProgressStatus = "InProgress";
    public const string DoneStatus = "Done";
    public const string CancelledStatus = "Cancelled";

    public Guid BookingId { get; set; }
    public string Status { get; set; } = PendingStatus;
    public Guid? CandidateRouteId { get; set; }
    public Guid? ResultRouteId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public Guid? InProgressByUserId { get; set; }
    public DateTimeOffset? InProgressAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? Notes { get; set; }

    public Booking Booking { get; set; } = null!;
    public Route? CandidateRoute { get; set; }
    public Route? ResultRoute { get; set; }
    public User? RequestedByUser { get; set; }
    public User? InProgressByUser { get; set; }
    public ICollection<CharterRouteDrawRequestStop> Stops { get; set; } = new List<CharterRouteDrawRequestStop>();

    public static bool IsOpenStatus(string? status) =>
        string.Equals(status, PendingStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, InProgressStatus, StringComparison.OrdinalIgnoreCase);
}
