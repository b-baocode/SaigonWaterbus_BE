using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class PointTransaction : BaseGuidEntity
{
    public Guid UserId { get; set; }
    public Guid? BookingId { get; set; }
    public string TransactionType { get; set; } = null!;
    public int Points { get; set; }
    public int BalanceAfter { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public Booking? Booking { get; set; }
}
