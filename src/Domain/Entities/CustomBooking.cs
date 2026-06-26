using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class CustomBooking : Booking
{
    public Guid? BoatId { get; set; }
    public Guid? FromStationId { get; set; }
    public Guid? ToStationId { get; set; }
    public DateOnly DepartureDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public BoatRentalUnit RentalUnit { get; set; } = BoatRentalUnit.Day;
    public int DurationValue { get; set; }
    public int PassengerCount { get; set; }
    public int AdultCount { get; set; }
    public int ChildCount { get; set; }
    public int? PreferredNumberOfDecks { get; set; }
    public SeatSetupType? PreferredSeatSetupType { get; set; }
    public string? BoatRequirements { get; set; }
    public string? SpecialRequests { get; set; }

    ///
    public DateTimeOffset? HoldExpiresAt { get; set; }

    public Boat? Boat { get; set; }
    public Station? FromStation { get; set; }
    public Station? ToStation { get; set; }
    public ICollection<CustomBookingItineraryStop> ItineraryStops { get; set; } = new List<CustomBookingItineraryStop>();
}
