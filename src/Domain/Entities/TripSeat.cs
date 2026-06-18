namespace SaigonWaterbus.Domain.Entities;

// Một instance ghế cho một chuyến cụ thể.
// Mỗi trip có TripSeat = danh sách ghế available để khách đặt.
public class TripSeat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public Guid SeatId { get; set; }

    public Trip Trip { get; set; } = null!;
    public Seat Seat { get; set; } = null!;
}
