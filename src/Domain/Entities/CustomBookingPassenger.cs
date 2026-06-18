using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class CustomBookingPassenger : BaseGuidAuditableEntity
{
    public Guid CustomBookingRequestId { get; set; }

    public CustomBookingRequest CustomBookingRequest { get; set; } = null!;

    public int PassengerOrder { get; set; }

    public string FullName { get; set; } = null!;

    public CustomBookingPassengerType PassengerType { get; set; }

    public DateOnly DateOfBirth { get; set; }
}
