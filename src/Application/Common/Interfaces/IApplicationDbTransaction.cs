namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IApplicationDbTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}
