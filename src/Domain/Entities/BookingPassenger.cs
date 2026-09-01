using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class BookingPassenger : BaseGuidEntity
{
    public Guid BookingId { get; set; }
    public string FullName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public int? BirthYear { get; set; }
    public string? Gender { get; set; }
    public string? Nationality { get; set; }
    public string? PassengerType { get; set; }
    public string? Note { get; set; }
    public string ApprovalStatus { get; set; } = "Approved";
    public Guid? RequestBatchId { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewNote { get; set; }
    public Guid? TripId { get; set; }
    public Guid? TripSeatId { get; set; }
    // Physical seat reserved for a fully-paid charter booking before Operations creates its trip.
    // It is later mapped to the matching TripSeat without changing the passenger's seat code.
    public Guid? CharterSeatId { get; set; }
    public decimal? UnitPrice { get; set; }

    // Chặng đi của hành khách trên trip (trạm lên/xuống). Null = chiếm ghế cả trip
    // (dữ liệu cũ và trip sightseeing). StopOrder snapshot từ route_stops lúc đặt,
    // dùng check hai vé cùng ghế có giao chặng hay không mà không cần join route.
    public Guid? FromStationId { get; set; }
    public Guid? ToStationId { get; set; }
    public int? FromStopOrder { get; set; }
    public int? ToStopOrder { get; set; }

    public Booking Booking { get; set; } = null!;
    public Trip? Trip { get; set; }
    public TripSeat? TripSeat { get; set; }
    public Seat? CharterSeat { get; set; }
    public Station? FromStation { get; set; }
    public Station? ToStation { get; set; }
    public User? RequestedByUser { get; set; }
    public User? ReviewedByUser { get; set; }
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}
