namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IPaymentProcessingLock
{
    Task<IPaymentProcessingLockHandle> TryAcquireAsync(
        string paymentCode,
        CancellationToken cancellationToken);
}

public interface IPaymentProcessingLockHandle : IAsyncDisposable
{
    bool Acquired { get; }
}
