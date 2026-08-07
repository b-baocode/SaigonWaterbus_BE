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

    public DbSet<CharterBookingBoat> CharterBookingBoats => Set<CharterBookingBoat>();

    public DbSet<CharterRouteDrawRequest> CharterRouteDrawRequests => Set<CharterRouteDrawRequest>();

    public DbSet<CharterRouteDrawRequestStop> CharterRouteDrawRequestStops => Set<CharterRouteDrawRequestStop>();

    public DbSet<Seat> Seats => Set<Seat>();

    public DbSet<Station> Stations => Set<Station>();

    public DbSet<Route> Routes => Set<Route>();

    public DbSet<RouteStop> RouteStops => Set<RouteStop>();

    public DbSet<LandmarkAudio> LandmarkAudios => Set<LandmarkAudio>();

    public DbSet<Voice> Voices => Set<Voice>();

    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<Promotion> Promotions => Set<Promotion>();

    public DbSet<FarePolicy> FarePolicies => Set<FarePolicy>();

    public DbSet<FareAdjustment> FareAdjustments => Set<FareAdjustment>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<BookingPassenger> BookingPassengers => Set<BookingPassenger>();

    public DbSet<InsurancePackage> InsurancePackages => Set<InsurancePackage>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<TicketScanEvent> TicketScanEvents => Set<TicketScanEvent>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<StaffWorkAssignment> StaffWorkAssignments => Set<StaffWorkAssignment>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<UserStationAssignment> UserStationAssignments => Set<UserStationAssignment>();

    public DbSet<Landmark> Landmarks => Set<Landmark>();

    public DbSet<KnowledgeEntry> KnowledgeEntries => Set<KnowledgeEntry>();

    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();

    public DbSet<SaigonWaterbus.Domain.Entities.ChatMessage> ChatMessages => Set<SaigonWaterbus.Domain.Entities.ChatMessage>();

    public DbSet<GpsDevice> GpsDevices => Set<GpsDevice>();

    public DbSet<BoatLatestLocation> BoatLatestLocations => Set<BoatLatestLocation>();

    public DbSet<GpsTrackingSession> GpsTrackingSessions => Set<GpsTrackingSession>();

    public DbSet<GpsTrackPoint> GpsTrackPoints => Set<GpsTrackPoint>();

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

    /// <summary>
    /// ORDER BY trip_seat_id để mọi request khoá theo cùng một thứ tự — tránh deadlock khi
    /// hai booking cùng đụng một tập ghế nhưng liệt kê ngược nhau.
    /// </summary>
    public async Task LockTripSeatsAsync(IReadOnlyList<Guid> tripSeatIds, CancellationToken cancellationToken)
    {
        if (tripSeatIds.Count == 0 || !Database.IsNpgsql())
        {
            return;
        }

        await Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM trip_seats WHERE trip_seat_id = ANY({0}) ORDER BY trip_seat_id FOR UPDATE",
            [tripSeatIds.Distinct().ToArray()],
            cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
