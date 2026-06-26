using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Common.Interfaces;

public sealed class NullOtpCache : IOtpCache
{
    public static readonly NullOtpCache Instance = new();

    private NullOtpCache() { }

    public Task StoreAsync(OtpChallenge challenge, string codeHash, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<string?> GetCodeHashAsync(Guid challengeId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    public Task RemoveAsync(Guid challengeId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class NullBoatHoldService : IBoatHoldService
{
    public static readonly NullBoatHoldService Instance = new();

    private NullBoatHoldService() { }

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

public sealed class NullPaymentProcessingLock : IPaymentProcessingLock
{
    public static readonly NullPaymentProcessingLock Instance = new();

    private static readonly IPaymentProcessingLockHandle Handle = new NullPaymentProcessingLockHandle();

    private NullPaymentProcessingLock() { }

    public Task<IPaymentProcessingLockHandle> TryAcquireAsync(
        string paymentCode,
        CancellationToken cancellationToken) =>
        Task.FromResult(Handle);

    private sealed class NullPaymentProcessingLockHandle : IPaymentProcessingLockHandle
    {
        public bool Acquired => true;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
