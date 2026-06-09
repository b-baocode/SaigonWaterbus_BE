using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class BookingItem : BaseGuidEntity
{
    public Guid BookingId { get; set; }
    public Guid TripId { get; set; }
    public Guid TicketTypeId { get; set; }
    public Guid FromTripStopId { get; set; }
    public Guid ToTripStopId { get; set; }
    public string PassengerName { get; set; } = null!;
    public string? PassengerPhone { get; set; }
    public decimal UnitPrice { get; set; }
    public BookingItemStatus ItemStatus { get; set; } = BookingItemStatus.Active;

    public Booking Booking { get; set; } = null!;
    public Trip Trip { get; set; } = null!;
    public TicketType TicketType { get; set; } = null!;
    public TripStop FromTripStop { get; set; } = null!;
    public TripStop ToTripStop { get; set; } = null!;
}
