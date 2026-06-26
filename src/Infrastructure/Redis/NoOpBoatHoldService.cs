using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Redis;

public sealed class NoOpBoatHoldService : IBoatHoldService
{
    public Task<bool> TryHoldAsync(
        Guid bookingId,
        Guid boatId,
        DateOnly departureDate,
        TimeOnly? startTime,
        BoatRentalUnit rentalUnit,
        int durationValue,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Task ReleaseAsync(
        Guid bookingId,
        Guid? boatId,
        DateOnly departureDate,
        TimeOnly? startTime,
        BoatRentalUnit rentalUnit,
        int durationValue,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
