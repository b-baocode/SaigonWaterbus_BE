using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class TripSeat : BaseGuidEntity
{
    public const string StatusAvailable = "Available";
    public const string StatusBlocked = "Blocked";
    public const string StatusBooked = "Booked";

    public Guid TripId { get; set; }
    public Guid SeatId { get; set; }
    public string Status { get; set; } = StatusAvailable;
    public decimal? Price { get; set; }

    public Trip Trip { get; set; } = null!;
    public Seat Seat { get; set; } = null!;
}
