using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Boat : BaseAuditableEntity
{
    public string Code { get; set; } = null!;

    public string? RegistrationNumber { get; set; }

    public string Name { get; set; } = null!;

    public BoatStatus Status { get; set; } = BoatStatus.Active;

    public DateTimeOffset? MaintenanceStartedAt { get; set; }

    public int SeatCount { get; set; }

    public int NumberOfDecks { get; set; }

    public SeatSetupType SeatSetupType { get; set; } = SeatSetupType.FullStandard;

    public bool SeatsConfigured { get; set; }

    public int? MaxSpeedKmh { get; set; }

    public int? YearBuilt { get; set; }

    public string? ImageUrl { get; set; }

    public string[] ImageUrls { get; set; } = [];

    public string? ImagePublicId { get; set; }

    public BoatDocument[] Documents { get; set; } = [];

    public decimal? HourlyRentalPrice { get; set; }

    public decimal? DailyRentalPrice { get; set; }

    public string Currency { get; set; } = "VND";

    public string? Description { get; set; }

    public ICollection<Seat> Seats { get; set; } = new List<Seat>();

    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
    
    public ICollection<StaffWorkAssignment> WorkAssignments { get; set; } = new List<StaffWorkAssignment>();
}

// Stored inside boats.documents JSON; this is not mapped as a separate table.
public sealed class BoatDocument
{
    public Guid Id { get; set; }

    public BoatDocumentType Type { get; set; }

    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long FileSize { get; set; }

    public string FileUrl { get; set; } = null!;

    public string StorageKey { get; set; } = null!;

    public DateOnly? IssuedDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}
