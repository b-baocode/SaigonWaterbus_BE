using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class CharterBookingBoat : BaseGuidAuditableEntity
{
    public Guid BookingId { get; set; }
    public Guid BoatId { get; set; }
    public Guid? TripId { get; set; }
    public int BoatOrder { get; set; }
    public SeatSetupType SeatSetupType { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ChargeableDurationValue { get; set; }
    public decimal SubtotalAmount { get; set; }

    public Booking Booking { get; set; } = null!;
    public Boat Boat { get; set; } = null!;
    public Trip? Trip { get; set; }
}
