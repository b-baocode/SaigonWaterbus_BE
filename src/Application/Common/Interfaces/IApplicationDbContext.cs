using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Role> Roles { get; }

    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<Boat> Boats { get; }

    DbSet<CharterBookingBoat> CharterBookingBoats { get; }

    DbSet<Seat> Seats { get; }

    DbSet<Ticket> Tickets { get; }

    DbSet<TicketScanEvent> TicketScanEvents { get; }

    DbSet<Incident> Incidents { get; }

    DbSet<StaffWorkAssignment> StaffWorkAssignments { get; }

    DbSet<GpsDevice> GpsDevices { get; }

    DbSet<BoatLatestLocation> BoatLatestLocations { get; }

    DbSet<GpsTrackingSession> GpsTrackingSessions { get; }

    DbSet<GpsTrackPoint> GpsTrackPoints { get; }

    DbSet<InsurancePackage> InsurancePackages { get; }

    DbSet<FareAdjustment> FareAdjustments { get; }

    DbSet<TEntity> Set<TEntity>()
        where TEntity : class;

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);

    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Khoá các hàng trip_seats (SELECT … FOR UPDATE) để tuần tự hoá việc bán cùng một ghế.
    /// Phải gọi TRONG transaction — khoá được giữ tới khi commit. Provider không quan hệ
    /// (test in-memory) thì đây là no-op.
    /// </summary>
    Task LockTripSeatsAsync(IReadOnlyList<Guid> tripSeatIds, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
