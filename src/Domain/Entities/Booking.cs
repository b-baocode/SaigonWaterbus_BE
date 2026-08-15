using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Booking : BaseGuidAuditableEntity
{
    public const string SeatBookingType = "SeatBooking";
    public const string CharterBookingType = "CharterBooking";

    public Guid? UserId { get; set; }
    public Guid? PromotionId { get; set; }
    public Guid? TripId { get; set; }


    public Guid? ReturnTripId { get; set; }
    public Guid? CharterRouteId { get; set; }
    public Guid? AssignedManagerId { get; set; }

    /// <summary>Staff bán vé tại quầy. Khác null = booking bán tại quầy (khách có thể không có tài khoản).</summary>
    public Guid? SoldByStaffId { get; set; }
    public string BookingType { get; set; } = SeatBookingType;
    public string BookingCode { get; set; } = null!;
    public string? CharterBookingQrToken { get; set; }
    public string ContactName { get; set; } = null!;
    public string ContactPhone { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public BookingStatus BookingStatus { get; set; } = BookingStatus.PendingPayment;
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    private string _paymentStatus = BookingPaymentStatusExtensions.UnpaidValue;
    public string PaymentStatus
    {
        get => _paymentStatus;
        set => _paymentStatus = value ?? BookingPaymentStatusExtensions.UnpaidValue;
    }
    public BookingPaymentStatus PaymentStatusEnum
    {
        get => _paymentStatus.ToBookingPaymentStatus();
        set => _paymentStatus = value.ToDbValue();
    }
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
    public int? RequestedBoatCount { get; set; }
    public string? RequestedBoatDecks { get; set; }
    public string? RequestedBoatTypes { get; set; }
    public SeatSetupType? PreferredSeatSetupType { get; set; }
    public string? BoatRequirements { get; set; }
    public string? SpecialRequests { get; set; }
    public BookingInsuranceSnapshot? InsuranceSnapshot { get; set; }
    public DateTimeOffset? HoldExpiresAt { get; set; }

    /// <summary>Audit: nguon nao da dua booking sang trang thai Completed (vi du "TripCompleted:7KMKQ").</summary>
    public string? CompletionSource { get; set; }

    /// <summary>Audit: thoi diem booking duoc danh dau hoan tat.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public User? User { get; set; }
    public Promotion? Promotion { get; set; }
    public Trip? Trip { get; set; }
    public Trip? ReturnTrip { get; set; }
    public Route? CharterRoute { get; set; }
    public User? AssignedManager { get; set; }
    public User? SoldByStaff { get; set; }
    public Boat? Boat { get; set; }
    public Station? FromStation { get; set; }
    public Station? ToStation { get; set; }
    public ICollection<BookingPassenger> Passengers { get; set; } = new List<BookingPassenger>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
    public ICollection<BookingItineraryStop> ItineraryStops { get; set; } = new List<BookingItineraryStop>();
    public ICollection<CharterBookingBoat> CharterBoats { get; set; } = new List<CharterBookingBoat>();

    public static bool IsCharterBookingType(string? bookingType) =>
        string.Equals(bookingType, CharterBookingType, StringComparison.OrdinalIgnoreCase);
}
