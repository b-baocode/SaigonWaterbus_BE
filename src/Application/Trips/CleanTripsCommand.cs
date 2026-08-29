using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin")]
public sealed record CleanTripsCommand(
    DateOnly OperatingDate,
    bool ConfirmDelete) : IRequest<CleanTripsResult>;

public sealed record CleanTripsResult(
    int Deleted,
    int DeletedBookings = 0,
    int DeletedTickets = 0,
    int DeletedPayments = 0,
    int DeletedPassengers = 0);

public sealed class CleanTripsCommandValidator : AbstractValidator<CleanTripsCommand>
{
    public CleanTripsCommandValidator()
    {
        RuleFor(x => x.OperatingDate).NotEmpty();
        RuleFor(x => x.ConfirmDelete)
            .Equal(true)
            .WithMessage("confirmDelete phải là true để xác nhận xóa toàn bộ trip trong ngày.");
    }
}

public sealed class CleanTripsCommandHandler : IRequestHandler<CleanTripsCommand, CleanTripsResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ISeatHoldService _seatHoldService;

    public CleanTripsCommandHandler(
        IApplicationDbContext context,
        ISeatHoldService? seatHoldService = null)
    {
        _context = context;
        _seatHoldService = seatHoldService ?? NullSeatHoldService.Instance;
    }

    public async Task<CleanTripsResult> Handle(CleanTripsCommand request, CancellationToken cancellationToken)
    {
        var trips = await _context.Set<Trip>()
            .Where(t => t.OperatingDate == request.OperatingDate)
            .ToListAsync(cancellationToken);

        if (trips.Count == 0)
            return new CleanTripsResult(0);

        var tripIds = trips.Select(t => t.Id).ToList();
        foreach (var tripId in tripIds)
        {
            await _seatHoldService.ClearTripAsync(tripId, cancellationToken);
        }

        var bookingIds = await ResolveRelatedBookingIdsAsync(trips, tripIds, cancellationToken);
        var bookings = bookingIds.Count == 0
            ? []
            : await _context.Set<Booking>()
                .Where(x => bookingIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var tickets = bookingIds.Count == 0
            ? []
            : await _context.Set<Ticket>()
                .Where(x => bookingIds.Contains(x.BookingId))
                .ToListAsync(cancellationToken);
        var payments = bookingIds.Count == 0
            ? []
            : await _context.Set<Payment>()
                .Where(x => bookingIds.Contains(x.BookingId))
                .ToListAsync(cancellationToken);
        var passengers = bookingIds.Count == 0
            ? []
            : await _context.Set<BookingPassenger>()
                .Where(x => bookingIds.Contains(x.BookingId))
                .ToListAsync(cancellationToken);

        var ticketIds = tickets.Select(x => x.Id).ToList();
        var scanEvents = await _context.Set<TicketScanEvent>()
            .Where(x => (x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
                || (x.BookingId.HasValue && bookingIds.Contains(x.BookingId.Value))
                || (x.TicketId.HasValue && ticketIds.Contains(x.TicketId.Value)))
            .ToListAsync(cancellationToken);
        var reviews = await _context.Set<Review>()
            .Where(x => (x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
                || (x.BookingId.HasValue && bookingIds.Contains(x.BookingId.Value)))
            .ToListAsync(cancellationToken);
        var pointTransactions = await _context.Set<PointTransaction>()
            .Where(x => x.BookingId.HasValue && bookingIds.Contains(x.BookingId.Value))
            .ToListAsync(cancellationToken);

        await ReversePointBalancesAsync(pointTransactions, cancellationToken);

        var incidents = await _context.Set<Incident>()
            .Where(x => x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
            .ToListAsync(cancellationToken);
        var gpsSessions = await _context.Set<GpsTrackingSession>()
            .Where(x => x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
            .ToListAsync(cancellationToken);
        var gpsSessionIds = gpsSessions.Select(x => x.Id).ToList();
        var gpsTrackPoints = await _context.Set<GpsTrackPoint>()
            .Where(x => (x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
                || gpsSessionIds.Contains(x.SessionId))
            .ToListAsync(cancellationToken);
        var latestLocations = await _context.Set<BoatLatestLocation>()
            .Where(x => x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
            .ToListAsync(cancellationToken);
        foreach (var location in latestLocations)
        {
            location.TripId = null;
        }

        var seats = await _context.Set<TripSeat>()
            .Where(ts => tripIds.Contains(ts.TripId))
            .ToListAsync(cancellationToken);
        if (seats.Count > 0)
            _context.Set<TripSeat>().RemoveRange(seats);

        var tripStops = await _context.Set<TripStop>()
            .Where(ts => tripIds.Contains(ts.TripId))
            .ToListAsync(cancellationToken);
        if (tripStops.Count > 0)
            _context.Set<TripStop>().RemoveRange(tripStops);

        _context.Set<TicketScanEvent>().RemoveRange(scanEvents);
        _context.Set<Review>().RemoveRange(reviews);
        _context.Set<PointTransaction>().RemoveRange(pointTransactions);
        _context.Set<Incident>().RemoveRange(incidents);
        _context.Set<GpsTrackPoint>().RemoveRange(gpsTrackPoints);
        _context.Set<GpsTrackingSession>().RemoveRange(gpsSessions);
        _context.Set<Booking>().RemoveRange(bookings);
        _context.Set<Trip>().RemoveRange(trips);
        await _context.SaveChangesAsync(cancellationToken);

        return new CleanTripsResult(
            trips.Count,
            bookings.Count,
            tickets.Count,
            payments.Count,
            passengers.Count);
    }

    private async Task<List<Guid>> ResolveRelatedBookingIdsAsync(
        IReadOnlyList<Trip> trips,
        IReadOnlyList<Guid> tripIds,
        CancellationToken cancellationToken)
    {
        var directBookingIds = await _context.Set<Booking>()
            .Where(x => (x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
                || (x.ReturnTripId.HasValue && tripIds.Contains(x.ReturnTripId.Value)))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var passengerBookingIds = await _context.Set<BookingPassenger>()
            .Where(x => x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
            .Select(x => x.BookingId)
            .ToListAsync(cancellationToken);
        var charterBookingIds = await _context.Set<CharterBookingBoat>()
            .Where(x => x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
            .Select(x => x.BookingId)
            .ToListAsync(cancellationToken);

        return directBookingIds
            .Concat(passengerBookingIds)
            .Concat(charterBookingIds)
            .Concat(trips.Where(x => x.SourceBookingId.HasValue).Select(x => x.SourceBookingId!.Value))
            .Distinct()
            .ToList();
    }

    private async Task ReversePointBalancesAsync(
        IReadOnlyList<PointTransaction> transactions,
        CancellationToken cancellationToken)
    {
        if (transactions.Count == 0)
        {
            return;
        }

        var pointDeltaByUser = transactions
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.Sum(transaction => transaction.Points));
        var userIds = pointDeltaByUser.Keys.ToList();
        var users = await _context.Set<User>()
            .Where(x => userIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            user.PointBalance = Math.Max(0, user.PointBalance - pointDeltaByUser[user.Id]);
        }
    }
}
