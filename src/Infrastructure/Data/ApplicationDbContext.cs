using System.Reflection;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SaigonWaterbus.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<User> Users => Set<User>();

    public DbSet<UserStationAssignment> UserStationAssignments => Set<UserStationAssignment>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<WaterbusService> WaterbusServices => Set<WaterbusService>();

    public DbSet<Vessel> Vessels => Set<Vessel>();

    public DbSet<VesselRentalPrice> VesselRentalPrices => Set<VesselRentalPrice>();

    public DbSet<Seat> Seats => Set<Seat>();

    public DbSet<SeatType> SeatTypes => Set<SeatType>();

    public DbSet<ServiceSeatTypePrice> ServiceSeatTypePrices => Set<ServiceSeatTypePrice>();

    public DbSet<VesselDeckLayout> VesselDeckLayouts => Set<VesselDeckLayout>();

    public DbSet<VesselFacility> VesselFacilities => Set<VesselFacility>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();

    public DbSet<Station> Stations => Set<Station>();

    public DbSet<WaterwaySegment> WaterwaySegments => Set<WaterwaySegment>();

    public DbSet<Route> Routes => Set<Route>();

    public DbSet<RouteStop> RouteStops => Set<RouteStop>();

    public DbSet<RouteSegment> RouteSegments => Set<RouteSegment>();

    public DbSet<Landmark> Landmarks => Set<Landmark>();

    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<TripStop> TripStops => Set<TripStop>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<TicketType> TicketTypes => Set<TicketType>();

    public DbSet<FareMatrix> FareMatrices => Set<FareMatrix>();

    public DbSet<Promotion> Promotions => Set<Promotion>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<BookingItem> BookingItems => Set<BookingItem>();

    public DbSet<CustomBookingRequest> CustomBookingRequests => Set<CustomBookingRequest>();

    public DbSet<CustomBookingQuote> CustomBookingQuotes => Set<CustomBookingQuote>();

    public DbSet<CustomBookingItineraryStop> CustomBookingItineraryStops => Set<CustomBookingItineraryStop>();

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
