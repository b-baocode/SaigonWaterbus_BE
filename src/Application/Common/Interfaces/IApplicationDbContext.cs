using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Role> Roles { get; }

    DbSet<User> Users { get; }

    DbSet<UserStationAssignment> UserStationAssignments { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<WaterbusService> WaterbusServices { get; }

    DbSet<Vessel> Vessels { get; }

    DbSet<VesselRentalPrice> VesselRentalPrices { get; }

    DbSet<Seat> Seats { get; }

    DbSet<SeatType> SeatTypes { get; }

    DbSet<VesselDeckLayout> VesselDeckLayouts { get; }

    DbSet<VesselFacility> VesselFacilities { get; }

    DbSet<CustomBookingRequest> CustomBookingRequests { get; }

    DbSet<CustomBookingQuote> CustomBookingQuotes { get; }

    DbSet<CustomBookingItineraryStop> CustomBookingItineraryStops { get; }

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
