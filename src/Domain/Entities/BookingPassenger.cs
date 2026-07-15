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
    public decimal? UnitPrice { get; set; }

    public Booking Booking { get; set; } = null!;
    public Trip? Trip { get; set; }
    public TripSeat? TripSeat { get; set; }
    public User? RequestedByUser { get; set; }
    public User? ReviewedByUser { get; set; }
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}
