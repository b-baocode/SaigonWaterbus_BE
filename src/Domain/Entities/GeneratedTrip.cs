namespace SaigonWaterbus.Domain.Entities;

// Tracking record: lịch nào đã sinh trip cho ngày nào.
// Unique (TripScheduleId, OperatingDate) → idempotency cho background job.
public class GeneratedTrip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripScheduleId { get; set; }
    public Guid TripId { get; set; }
    public DateOnly OperatingDate { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    public TripSchedule TripSchedule { get; set; } = null!;
    public Trip Trip { get; set; } = null!;
}
