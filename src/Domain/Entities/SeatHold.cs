using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class SeatHold : BaseGuidEntity
{
    public Guid TripId { get; set; }
    public Guid SeatId { get; set; }
    public int UserId { get; set; }
    public DateTimeOffset HeldAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public SeatHoldStatus HoldStatus { get; set; } = SeatHoldStatus.Active;

    public Trip Trip { get; set; } = null!;
    public Seat Seat { get; set; } = null!;
    public User User { get; set; } = null!;
}
