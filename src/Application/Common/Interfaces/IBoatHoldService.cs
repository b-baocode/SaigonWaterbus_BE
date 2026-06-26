using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IBoatHoldService
{
    Task<bool> TryHoldAsync(
        Guid bookingId,
        Guid boatId,
        DateOnly departureDate,
        TimeOnly? startTime,
        BoatRentalUnit rentalUnit,
        int durationValue,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        Guid bookingId,
        Guid? boatId,
        DateOnly departureDate,
        TimeOnly? startTime,
        BoatRentalUnit rentalUnit,
        int durationValue,
        CancellationToken cancellationToken);
}
