using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Vessel : BaseAuditableEntity
{
    public string Code { get; set; } = null!;

    public string? RegistrationNumber { get; set; }

    public string Name { get; set; } = null!;

    public VesselStatus Status { get; set; } = VesselStatus.Active;

    public int SeatCount { get; set; }

    public int NumberOfDecks { get; set; }

    public SeatSetupType SeatSetupType { get; set; } = SeatSetupType.FullStandard;

    public bool SeatsConfigured { get; set; }

    public int? MaxSpeedKmh { get; set; }

    public int? YearBuilt { get; set; }

    public string? ImageUrl { get; set; }

    public string? ImagePublicId { get; set; }

    public decimal? HourlyRentalPrice { get; set; }

    public decimal? DailyRentalPrice { get; set; }

    public string Currency { get; set; } = "VND";

    public string? Description { get; set; }

    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
}
