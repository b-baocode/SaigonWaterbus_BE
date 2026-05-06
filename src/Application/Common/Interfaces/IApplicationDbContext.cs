using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Role> Roles { get; }

    DbSet<User> Users { get; }

    DbSet<StaffPosition> StaffPositions { get; }

    DbSet<UserPosition> UserPositions { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<TEntity> Set<TEntity>()
        where TEntity : class;

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);

    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
