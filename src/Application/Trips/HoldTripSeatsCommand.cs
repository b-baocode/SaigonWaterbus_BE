using FluentValidation.Results;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

public sealed record HoldTripSeatsResult(
    IReadOnlyList<string> HeldSeatNumbers,
    IReadOnlyList<string> FailedSeatNumbers,
    DateTimeOffset HoldExpiresAt,
    int HoldTtlSeconds);

// FromStationCode/ToStationCode: chặng khách sẽ đi — bắt buộc với trip Regular (ghế bán theo chặng),
// bỏ trống với trip sightseeing (giữ ghế cả trip).
public sealed record HoldTripSeatsCommand(
    Guid TripId,
    IReadOnlyList<string> SeatNumbers,
    string? FromStationCode = null,
    string? ToStationCode = null) : IRequest<HoldTripSeatsResult>;

public sealed record ReleaseTripSeatsCommand(
    Guid TripId,
    IReadOnlyList<string> SeatNumbers) : IRequest;

public sealed class HoldTripSeatsCommandValidator : AbstractValidator<HoldTripSeatsCommand>
{
    public HoldTripSeatsCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.SeatNumbers).NotEmpty().WithMessage("Phải chọn ít nhất một ghế.")
            .Must(x => x.Count <= 10).WithMessage("Tối đa 10 ghế một lần giữ.");
        RuleForEach(x => x.SeatNumbers).NotEmpty().MaximumLength(30);
    }
}

public sealed class ReleaseTripSeatsCommandValidator : AbstractValidator<ReleaseTripSeatsCommand>
{
    public ReleaseTripSeatsCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.SeatNumbers).NotEmpty().WithMessage("Phải chọn ít nhất một ghế.");
        RuleForEach(x => x.SeatNumbers).NotEmpty().MaximumLength(30);
    }
}

internal static class TripSeatResolutionSupport
{
    public sealed record ResolvedTripSeat(Seat Seat, TripSeat TripSeat);

    public static async Task<Trip> GetBookableTripAsync(
        IApplicationDbContext context,
        Guid tripId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // TripStops cần cho hạn đóng bán theo bến khách lên — xem BookingCutoffSupport.
        var trip = await context.Set<Trip>()
            .Include(t => t.TripStops)
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(t => t.Id == tripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        if (!TripBookingAvailabilitySupport.CanAcceptCustomerBooking(trip.TripStatus))
        {
            throw new ValidationException([new ValidationFailure("tripId",
                "Trip is not available for booking.")]);
        }

        // Hạn đóng bán kiểm sau khi resolve chặng (giờ rời bến khách lên), không kiểm ở đây.

        if (!trip.BoatId.HasValue)
        {
            throw new ValidationException([new ValidationFailure("tripId",
                "Trip has no boat assigned.")]);
        }

        return trip;
    }

    public static async Task<IReadOnlyList<ResolvedTripSeat>> ResolveTripSeatsAsync(
        IApplicationDbContext context,
        Trip trip,
        IReadOnlyList<string> seatNumbers,
        CancellationToken cancellationToken)
    {
        var requestedSeatCodes = seatNumbers
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var seatsByCode = await context.Set<Seat>()
            .AsNoTracking()
            .Where(x => x.BoatId == trip.BoatId!.Value && requestedSeatCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, cancellationToken);

        var missingSeat = requestedSeatCodes.FirstOrDefault(x => !seatsByCode.ContainsKey(x));
        if (missingSeat is not null)
        {
            throw new NotFoundException($"Seat '{missingSeat}' not found on this trip boat.");
        }

        var inactiveSeat = seatsByCode.Values.FirstOrDefault(x => !x.IsActive);
        if (inactiveSeat is not null)
        {
            throw new ValidationException([new ValidationFailure("seatNumbers",
                $"Seat '{inactiveSeat.Code}' is not available for booking.")]);
        }

        var seatIds = seatsByCode.Values.Select(x => x.Id).ToList();
        var tripSeatsBySeatId = await context.Set<TripSeat>()
            .AsNoTracking()
            .Where(x => x.TripId == trip.Id && seatIds.Contains(x.SeatId))
            .ToDictionaryAsync(x => x.SeatId, cancellationToken);

        var missingTripSeat = seatsByCode.Values.FirstOrDefault(x => !tripSeatsBySeatId.ContainsKey(x.Id));
        if (missingTripSeat is not null)
        {
            throw new ValidationException([new ValidationFailure("seatNumbers",
                $"Seat '{missingTripSeat.Code}' is not available on this trip.")]);
        }

        return requestedSeatCodes
            .Select(code => seatsByCode[code])
            .Select(seat => new ResolvedTripSeat(seat, tripSeatsBySeatId[seat.Id]))
            .ToList();
    }
}

public sealed class HoldTripSeatsCommandHandler : IRequestHandler<HoldTripSeatsCommand, HoldTripSeatsResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ISeatHoldService _seatHoldService;
    private readonly ITripSeatNotifier _tripSeatNotifier;

    public HoldTripSeatsCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ISeatHoldService? seatHoldService = null,
        ITripSeatNotifier? tripSeatNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _seatHoldService = seatHoldService ?? NullSeatHoldService.Instance;
        _tripSeatNotifier = tripSeatNotifier ?? NullTripSeatNotifier.Instance;
    }

    public async Task<HoldTripSeatsResult> Handle(HoldTripSeatsCommand request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var now = _timeProvider.GetUtcNow();
        var trip = await TripSeatResolutionSupport.GetBookableTripAsync(_context, request.TripId, now, cancellationToken);

        // Trip Regular bán ghế theo chặng nên bắt buộc truyền trạm lên/xuống;
        // sightseeing giữ nguyên giữ ghế cả trip.
        var usesSegments = Fares.DistanceFareSupport.UsesDistanceFare(trip);
        if (usesSegments && (string.IsNullOrWhiteSpace(request.FromStationCode)
                             || string.IsNullOrWhiteSpace(request.ToStationCode)))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.FromStationCode),
                "fromStationCode và toStationCode là bắt buộc khi giữ ghế trên trip Regular (ghế bán theo chặng).")]);
        }

        var segment = usesSegments
            ? TripSegmentSupport.Resolve(trip, request.FromStationCode, request.ToStationCode,
                nameof(request.FromStationCode), nameof(request.ToStationCode))
            : TripSegmentSupport.FullTrip;

        // Hạn đóng bán theo giờ tàu rời bến khách LÊN, không phải bến đầu tuyến.
        if (BookingCutoffSupport.IsPastCutoff(
                trip,
                segment.IsFullTrip ? null : segment.FromStopOrder,
                segment.IsFullTrip ? null : segment.ToStopOrder,
                now))
        {
            throw new ValidationException([new ValidationFailure("tripId", BookingCutoffSupport.CutoffMessage())]);
        }

        var resolvedSeats = await TripSeatResolutionSupport.ResolveTripSeatsAsync(
            _context, trip, request.SeatNumbers, cancellationToken);

        var blockedSeat = resolvedSeats.FirstOrDefault(x => x.TripSeat.Status == TripSeat.StatusBlocked);
        if (blockedSeat is not null)
        {
            throw new ValidationException([new ValidationFailure("seatNumbers",
                $"Seat '{blockedSeat.Seat.Code}' is not available for booking.")]);
        }

        var tripSeatIds = resolvedSeats.Select(x => x.TripSeat.Id).ToList();
        var occupiedTripSeatIds = (await _context.Set<BookingPassenger>()
                .Where(x => x.TripSeatId.HasValue && tripSeatIds.Contains(x.TripSeatId.Value))
                .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
                .Where(BookingSeatOccupancySupport.PassengerOverlapsSegment(segment.FromStopOrder, segment.ToStopOrder))
                .Select(x => x.TripSeatId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var bookedSeat = resolvedSeats.FirstOrDefault(x => occupiedTripSeatIds.Contains(x.TripSeat.Id));
        if (bookedSeat is not null)
        {
            throw new ValidationException([new ValidationFailure("seatNumbers",
                $"Seat '{bookedSeat.Seat.Code}' is already booked.")]);
        }

        var ttl = BookingSeatOccupancySupport.SeatPreHoldDuration;
        var failedTripSeatIds = (await _seatHoldService.TryHoldAsync(
                trip.Id, tripSeatIds, userId, segment.FromStopOrder, segment.ToStopOrder, ttl, cancellationToken))
            .ToHashSet();

        var heldSeatNumbers = resolvedSeats
            .Where(x => !failedTripSeatIds.Contains(x.TripSeat.Id))
            .Select(x => x.Seat.Code)
            .ToList();
        var failedSeatNumbers = resolvedSeats
            .Where(x => failedTripSeatIds.Contains(x.TripSeat.Id))
            .Select(x => x.Seat.Code)
            .ToList();

        if (heldSeatNumbers.Count > 0)
        {
            await _tripSeatNotifier.PublishSeatStatusChangedAsync(
                trip.Id,
                heldSeatNumbers
                    .Select(code => new TripSeatStatusChange(code, "Held",
                        segment.IsFullTrip ? null : segment.FromStopOrder,
                        segment.IsFullTrip ? null : segment.ToStopOrder))
                    .ToList(),
                cancellationToken);
        }

        return new HoldTripSeatsResult(
            heldSeatNumbers,
            failedSeatNumbers,
            now.Add(ttl),
            (int)ttl.TotalSeconds);
    }
}

public sealed class ReleaseTripSeatsCommandHandler : IRequestHandler<ReleaseTripSeatsCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ISeatHoldService _seatHoldService;
    private readonly ITripSeatNotifier _tripSeatNotifier;

    public ReleaseTripSeatsCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ISeatHoldService? seatHoldService = null,
        ITripSeatNotifier? tripSeatNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _seatHoldService = seatHoldService ?? NullSeatHoldService.Instance;
        _tripSeatNotifier = tripSeatNotifier ?? NullTripSeatNotifier.Instance;
    }

    public async Task Handle(ReleaseTripSeatsCommand request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var trip = await _context.Set<Trip>()
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        if (!trip.BoatId.HasValue)
        {
            return;
        }

        var resolvedSeats = await TripSeatResolutionSupport.ResolveTripSeatsAsync(
            _context, trip, request.SeatNumbers, cancellationToken);

        var heldSeats = await _seatHoldService.GetHeldSeatsAsync(trip.Id, cancellationToken);
        var mySeats = resolvedSeats
            .Where(x => heldSeats.TryGetValue(x.TripSeat.Id, out var holds)
                     && holds.Any(h => h.UserId == userId))
            .ToList();
        if (mySeats.Count == 0)
        {
            return;
        }

        await _seatHoldService.ReleaseAsync(
            trip.Id,
            mySeats.Select(x => x.TripSeat.Id).ToList(),
            userId,
            cancellationToken);

        // Nhả toàn bộ lượt giữ của user trên các ghế → phát một sự kiện cho từng chặng vừa nhả.
        var changes = mySeats
            .SelectMany(x => heldSeats[x.TripSeat.Id]
                .Where(h => h.UserId == userId)
                .Select(h => new TripSeatStatusChange(
                    x.Seat.Code, "Available",
                    h.FromStopOrder == int.MinValue ? null : h.FromStopOrder,
                    h.ToStopOrder == int.MaxValue ? null : h.ToStopOrder)))
            .ToList();
        await _tripSeatNotifier.PublishSeatStatusChangedAsync(trip.Id, changes, cancellationToken);
    }
}
