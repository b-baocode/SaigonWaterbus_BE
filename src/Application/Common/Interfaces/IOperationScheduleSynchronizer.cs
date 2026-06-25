namespace SaigonWaterbus.Application.Common.Interfaces;

///
public interface IOperationScheduleSynchronizer
{
    ///
    Task<int> SyncAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
