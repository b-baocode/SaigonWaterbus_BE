using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class TicketItem : BaseGuidEntity
{
    public Guid BookingId { get; set; }
    public Guid BookingPassengerId { get; set; }
    public Guid? TripSeatId { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? TicketTypeId { get; set; }

    public Booking Booking { get; set; } = null!;
    public BookingPassenger BookingPassenger { get; set; } = null!;
    public TripSeat? TripSeat { get; set; }
    public TicketType? TicketType { get; set; }
    public Ticket? Ticket { get; set; }
}
