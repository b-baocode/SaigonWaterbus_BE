using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class BookingPassenger : BaseGuidEntity
{
    public Guid BookingId { get; set; }
    public string FullName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? PassengerType { get; set; }
    public string? IdentityNumber { get; set; }

    public Booking Booking { get; set; } = null!;
    public ICollection<BookingTicket> Tickets { get; set; } = new List<BookingTicket>();
}
