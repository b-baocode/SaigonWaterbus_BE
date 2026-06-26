using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Redis;

public sealed class NoOpPaymentProcessingLock : IPaymentProcessingLock
{
    private static readonly IPaymentProcessingLockHandle Handle = new NoOpPaymentProcessingLockHandle();

    public Task<IPaymentProcessingLockHandle> TryAcquireAsync(
        string paymentCode,
        CancellationToken cancellationToken) =>
        Task.FromResult(Handle);

    private sealed class NoOpPaymentProcessingLockHandle : IPaymentProcessingLockHandle
    {
        public bool Acquired => true;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
