using Microsoft.EntityFrameworkCore.Storage;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class ApplicationDbTransaction : IApplicationDbTransaction
{
    private readonly IDbContextTransaction _transaction;

    public ApplicationDbTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public Task CommitAsync(CancellationToken cancellationToken) =>
        _transaction.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken) =>
        _transaction.RollbackAsync(cancellationToken);

    public ValueTask DisposeAsync() =>
        _transaction.DisposeAsync();
}
