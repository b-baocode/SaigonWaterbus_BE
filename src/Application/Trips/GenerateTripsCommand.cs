using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin,Manager")]
public sealed record GenerateTripsCommand(
    string RouteCode,
    string BoatCode,
    IReadOnlyList<TimeOnly> DepartureTimes,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<int>? DaysOfWeek = null,
    IReadOnlyList<TripSeatTypePriceInput>? SeatTypePrices = null) : IRequest<GenerateTripsResult>;

public sealed record GenerateTripsResult(
    int Created,
    int Skipped,
    int SkippedBoatBusy,
    IReadOnlyList<string> CreatedTripCodes);

public sealed class GenerateTripsCommandValidator : AbstractValidator<GenerateTripsCommand>
{
    public GenerateTripsCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DepartureTimes).NotEmpty().WithMessage("At least one departure time is required.");
        RuleFor(x => x.FromDate).NotEmpty();
        RuleFor(x => x.ToDate)
            .NotEmpty()
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("ToDate must be on or after FromDate.")
            .Must((cmd, to) => to.DayNumber - cmd.FromDate.DayNumber <= 365)
            .WithMessage("Cannot generate trips for more than 365 days at once.");
        RuleFor(x => x.DaysOfWeek)
            .Must(days => days == null || (days.Count > 0 && days.All(d => d is >= 0 and <= 6)))
            .WithMessage("DaysOfWeek values must be 0–6 (0=Sunday, 6=Saturday).")
            .When(x => x.DaysOfWeek is not null);
        RuleFor(x => x.SeatTypePrices!)
            .SetValidator(new TripSeatTypePricesValidator())
            .When(x => x.SeatTypePrices is not null);
    }
}

public sealed class GenerateTripsCommandHandler : IRequestHandler<GenerateTripsCommand, GenerateTripsResult>
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly IApplicationDbContext _context;

    public GenerateTripsCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<GenerateTripsResult> Handle(GenerateTripsCommand request, CancellationToken cancellationToken)
    {
        var routeCode = request.RouteCode.Trim().ToUpperInvariant();
        var boatCode = request.BoatCode.Trim().ToUpperInvariant();

        var route = await _context.Set<Route>()
            .Include(r => r.RouteStops.OrderBy(rs => rs.StopOrder))
                .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(r => r.RouteCode == routeCode && r.Status == "Active" && r.IsBookable, cancellationToken)
            ?? throw new NotFoundException($"Route '{routeCode}' not found, inactive, or not bookable.");

        if (route.RouteStops.Count < 2)
            throw new ValidationException([new ValidationFailure(nameof(request.RouteCode),
                "Route must have at least 2 stops.")]);

        var boat = await _context.Set<Boat>()
            .SingleOrDefaultAsync(x => x.Code == boatCode, cancellationToken)
            ?? throw new NotFoundException($"Boat '{boatCode}' not found.");

        if (boat.Status != BoatStatus.Active || !boat.SeatsConfigured)
            throw new ValidationException([new ValidationFailure(nameof(request.BoatCode),
                "Boat must be active and have configured seats.")]);

        var activeSeats = await _context.Set<Seat>()
            .Where(x => x.BoatId == boat.Id && x.IsActive)
            .ToListAsync(cancellationToken);

        if (activeSeats.Count == 0)
            throw new ValidationException([new ValidationFailure(nameof(request.BoatCode),
                "Boat has no active seats.")]);

        var existingDepartures = await _context.Set<Trip>()
            .AsNoTracking()
            .Where(t => t.RouteId == route.Id
                     && t.OperatingDate >= request.FromDate
                     && t.OperatingDate <= request.ToDate
                     && t.TripStatus != TripStatus.Cancelled)
            .Select(t => t.DepartureTime)
            .ToHashSetAsync(cancellationToken);

        var allowedDays = request.DaysOfWeek is { Count: > 0 }
            ? request.DaysOfWeek.Select(d => (DayOfWeek)d).ToHashSet()
            : null;

        // Lich chay hien co cua TAU (moi tuyen, khong chi tuyen nay) de chan double-booking.
        var rangeStart = new DateTimeOffset(
            request.FromDate.Year, request.FromDate.Month, request.FromDate.Day,
            0, 0, 0, VietnamOffset).ToUniversalTime();
        var rangeEnd = new DateTimeOffset(
            request.ToDate.Year, request.ToDate.Month, request.ToDate.Day,
            23, 59, 59, VietnamOffset).ToUniversalTime();

        var boatSchedule = (await _context.Set<Trip>()
                .AsNoTracking()
                .Where(t => t.BoatId == boat.Id
                    && t.TripStatus != TripStatus.Cancelled
                    && t.ArrivalTime >= rangeStart.AddDays(-1)
                    && t.DepartureTime <= rangeEnd.AddDays(1))
                .Select(t => new { t.DepartureTime, t.ArrivalTime })
                .ToListAsync(cancellationToken))
            .Select(t => (t.DepartureTime, t.ArrivalTime))
            .ToList();

        var skippedBoatBusy = 0;

        var resolveSeatPrice = await TripSeatPricingSupport.BuildSeatPriceResolverAsync(
            _context, request.SeatTypePrices, activeSeats, cancellationToken);

        var tripsToAdd = new List<Trip>();
        var tripSeatsToAdd = new List<TripSeat>();
        var createdCodes = new List<string>();
        int skipped = 0;

        for (var date = request.FromDate; date <= request.ToDate; date = date.AddDays(1))
        {
            if (allowedDays is not null && !allowedDays.Contains(date.DayOfWeek))
                continue;

            foreach (var time in request.DepartureTimes)
            {
                var departureTime = new DateTimeOffset(
                    date.Year, date.Month, date.Day,
                    time.Hour, time.Minute, 0,
                    VietnamOffset).ToUniversalTime();

                if (existingDepartures.Contains(departureTime))
                {
                    skipped++;
                    continue;
                }

                var arrivalTime = ComputeArrivalTime(departureTime, route.RouteStops);

                // Tau da ban trong khung gio nay (ke ca chuyen vua sinh trong lo nay) -> bo qua.
                if (boatSchedule.Any(x => TripScheduleSupport.ConflictsWithBuffer(
                        x.DepartureTime, x.ArrivalTime, departureTime, arrivalTime)))
                {
                    skippedBoatBusy++;
                    continue;
                }

                var tripCode = $"TR-{date:yyyyMMdd}-{routeCode}-{time:HHmm}";

                var trip = new Trip
                {
                    RouteId = route.Id,
                    BoatId = boat.Id,
                    TripCode = tripCode,
                    OperatingDate = date,
                    DepartureTime = departureTime,
                    ArrivalTime = arrivalTime,
                    CapacitySnapshot = activeSeats.Count,
                    TripStatus = TripStatus.Scheduled
                };

                tripsToAdd.Add(trip);
                existingDepartures.Add(departureTime);
                boatSchedule.Add((departureTime, arrivalTime));

                foreach (var seat in activeSeats)
                    tripSeatsToAdd.Add(new TripSeat
                    {
                        TripId = trip.Id,
                        SeatId = seat.Id,
                        Price = resolveSeatPrice(seat)
                    });

                createdCodes.Add(tripCode);
            }
        }

        if (tripsToAdd.Count > 0)
        {
            _context.Set<Trip>().AddRange(tripsToAdd);
            _context.Set<TripSeat>().AddRange(tripSeatsToAdd);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new GenerateTripsResult(tripsToAdd.Count, skipped, skippedBoatBusy, createdCodes);
    }

    private static DateTimeOffset ComputeArrivalTime(
        DateTimeOffset departureTime,
        IEnumerable<RouteStop> routeStops)
    {
        var current = departureTime;
        foreach (var stop in routeStops.OrderBy(rs => rs.StopOrder).SkipLast(1))
        {
            current = current.AddMinutes(stop.StandardTravelMin ?? 15);
        }
        return current;
    }
}
