using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class OperationScheduleEntry : BaseGuidAuditableEntity
{
    public OperationScheduleSourceType SourceType { get; set; }

    public Guid SourceId { get; set; }

    public string SourceCode { get; set; } = null!;

    public string Title { get; set; } = null!;

    public Guid? ServiceId { get; set; }

    public string? ServiceCode { get; set; }

    public string? ServiceName { get; set; }

    public BookingMode? BookingMode { get; set; }

    public Guid? VesselId { get; set; }

    public string? VesselCode { get; set; }

    public string? VesselName { get; set; }

    public Guid? RouteId { get; set; }

    public string? RouteCode { get; set; }

    public string? RouteName { get; set; }

    public Guid? FromStationId { get; set; }

    public string? FromStationCode { get; set; }

    public string FromLocation { get; set; } = null!;

    public Guid? ToStationId { get; set; }

    public string? ToStationCode { get; set; }

    public string ToLocation { get; set; } = null!;

    public DateOnly OperatingDate { get; set; }

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public string Status { get; set; } = null!;

    public string ScheduleState { get; set; } = null!;

    public string OperationStatus { get; set; } = null!;

    public string? PaymentStage { get; set; }

    public DateTimeOffset? RemainingPaymentDeadline { get; set; }

    public bool IsPaymentOverdue { get; set; }

    public int DelayMinutes { get; set; }

    public string? DelayReason { get; set; }

    public DateTimeOffset? AdjustedStartAt { get; set; }

    public DateTimeOffset? AdjustedEndAt { get; set; }

    public DateTimeOffset? ActualStartAt { get; set; }

    public DateTimeOffset? ActualEndAt { get; set; }

    public Guid? CustomerUserId { get; set; }

    public Guid? AssignedManagerUserId { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
