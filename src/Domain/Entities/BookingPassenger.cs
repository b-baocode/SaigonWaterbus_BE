using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class BookingPassenger : BaseGuidEntity
{
    public Guid BookingId { get; set; }
    public string FullName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public int? BirthYear { get; set; }
    public string? Gender { get; set; }
    public string? Nationality { get; set; }
    public string? PassengerType { get; set; }
    public string? Note { get; set; }

    public Booking Booking { get; set; } = null!;
    public ICollection<TicketItem> TicketItems { get; set; } = new List<TicketItem>();
}
