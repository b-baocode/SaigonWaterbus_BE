using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Boat> Boats => Set<Boat>();

    public DbSet<Seat> Seats => Set<Seat>();

    public DbSet<Station> Stations => Set<Station>();

    public DbSet<Route> Routes => Set<Route>();

    public DbSet<RouteStop> RouteStops => Set<RouteStop>();

    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<Promotion> Promotions => Set<Promotion>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<BookingPassenger> BookingPassengers => Set<BookingPassenger>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<BoatStaffAssignment> BoatStaffAssignments => Set<BoatStaffAssignment>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<UserStationAssignment> UserStationAssignments => Set<UserStationAssignment>();

    public DbSet<Landmark> Landmarks => Set<Landmark>();

    public DbSet<GpsDevice> GpsDevices => Set<GpsDevice>();

    public DbSet<BoatLatestLocation> BoatLatestLocations => Set<BoatLatestLocation>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        var executionStrategy = Database.CreateExecutionStrategy();

        return executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        var executionStrategy = Database.CreateExecutionStrategy();

        return executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
