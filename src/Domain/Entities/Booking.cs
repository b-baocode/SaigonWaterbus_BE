using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Booking : BaseGuidAuditableEntity
{
    public const string SeatBookingType = "SeatBooking";
    public const string CustomBookingType = "CustomBooking";

    public Guid? UserId { get; set; }
    public Guid? PromotionId { get; set; }
    public Guid? TripId { get; set; }
    public string BookingType { get; set; } = SeatBookingType;
    public string BookingCode { get; set; } = null!;
    public string? CustomBookingQrToken { get; set; }
    public string ContactName { get; set; } = null!;
    public string ContactPhone { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public BookingStatus BookingStatus { get; set; } = BookingStatus.PendingPayment;
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = "Unpaid";
    public decimal DepositAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public int PointsUsed { get; set; } = 0;
    public int PointsEarned { get; set; } = 0;

    public Guid? BoatId { get; set; }
    public Guid? FromStationId { get; set; }
    public Guid? ToStationId { get; set; }
    public DateOnly? DepartureDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public BoatRentalUnit? RentalUnit { get; set; }
    public int? DurationValue { get; set; }
    public int? PassengerCount { get; set; }
    public int? AdultCount { get; set; }
    public int? ChildCount { get; set; }
    public int? PreferredNumberOfDecks { get; set; }
    public SeatSetupType? PreferredSeatSetupType { get; set; }
    public string? BoatRequirements { get; set; }
    public string? SpecialRequests { get; set; }
    public DateTimeOffset? HoldExpiresAt { get; set; }

    public User? User { get; set; }
    public Promotion? Promotion { get; set; }
    public Trip? Trip { get; set; }
    public Boat? Boat { get; set; }
    public Station? FromStation { get; set; }
    public Station? ToStation { get; set; }
    public ICollection<BookingPassenger> Passengers { get; set; } = new List<BookingPassenger>();
    public ICollection<TicketItem> TicketItems { get; set; } = new List<TicketItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
    public ICollection<BookingItineraryStop> ItineraryStops { get; set; } = new List<BookingItineraryStop>();
}
