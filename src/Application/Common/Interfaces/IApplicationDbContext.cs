using Microsoft.EntityFrameworkCore;

namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<TEntity> Set<TEntity>()
        where TEntity : class;

    Task<IApplicationDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
