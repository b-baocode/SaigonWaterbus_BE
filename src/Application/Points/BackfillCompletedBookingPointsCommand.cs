using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Points;

public sealed record BackfillCompletedBookingPointsResultDto(
    int CandidateBookingCount,
    int AwardedBookingCount,
    int SkippedBookingCount,
    int TotalPointsAwarded);

[Authorize(Roles = "Admin")]
public sealed record BackfillCompletedBookingPointsCommand : IRequest<BackfillCompletedBookingPointsResultDto>;

public sealed class BackfillCompletedBookingPointsCommandHandler
    : IRequestHandler<BackfillCompletedBookingPointsCommand, BackfillCompletedBookingPointsResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public BackfillCompletedBookingPointsCommandHandler(
        IApplicationDbContext context,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BackfillCompletedBookingPointsResultDto> Handle(
        BackfillCompletedBookingPointsCommand request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var candidates = await LoadCompletedBookingCandidatesAsync(cancellationToken);
        var previousPointsEarned = candidates.ToDictionary(x => x.Id, x => x.PointsEarned);

        foreach (var booking in candidates)
        {
            await PointSupport.AwardCompletionPointsAsync(
                _context,
                booking,
                now,
                cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var totalPointsAwarded = candidates.Sum(x => x.PointsEarned - previousPointsEarned[x.Id]);
        var awardedBookingCount = candidates.Count(x => x.PointsEarned > previousPointsEarned[x.Id]);

        return new BackfillCompletedBookingPointsResultDto(
            candidates.Count,
            awardedBookingCount,
            candidates.Count - awardedBookingCount,
            totalPointsAwarded);
    }

    private async Task<List<Booking>> LoadCompletedBookingCandidatesAsync(CancellationToken cancellationToken)
    {
        var seatBookings = await _context.Set<Booking>()
            .Include(x => x.Passengers)
            .Where(x => x.UserId != null
                && x.PointsEarned == 0
                && x.BookingType == Booking.SeatBookingType
                && (x.BookingStatus == BookingStatus.Completed || x.BookingStatus == BookingStatus.Confirmed)
                && (x.TripId != null
                    || x.ReturnTripId != null
                    || x.Passengers.Any(p => p.TripId != null)))
            .ToListAsync(cancellationToken);

        var linkedTripIds = seatBookings
            .SelectMany(ResolveLinkedTripIds)
            .Distinct()
            .ToList();
        var completedTripIds = linkedTripIds.Count == 0
            ? []
            : await _context.Set<Trip>()
                .Where(x => linkedTripIds.Contains(x.Id) && x.TripStatus == TripStatus.Completed)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        var completedTripIdSet = completedTripIds.ToHashSet();

        var completedSeatBookings = seatBookings
            .Where(x => IsCompletedSeatBooking(x, completedTripIdSet))
            .ToList();

        var completedCharterBookings = await _context.Set<Booking>()
            .Where(x => x.UserId != null
                && x.PointsEarned == 0
                && x.BookingType == Booking.CharterBookingType
                && x.BookingStatus == BookingStatus.Completed)
            .ToListAsync(cancellationToken);

        return completedSeatBookings
            .Concat(completedCharterBookings)
            .DistinctBy(x => x.Id)
            .ToList();
    }

    private static bool IsCompletedSeatBooking(Booking booking, IReadOnlySet<Guid> completedTripIds)
    {
        if (booking.BookingStatus == BookingStatus.Completed)
        {
            return true;
        }

        var linkedTripIds = ResolveLinkedTripIds(booking).ToArray();
        return linkedTripIds.Length > 0 && linkedTripIds.All(completedTripIds.Contains);
    }

    private static IEnumerable<Guid> ResolveLinkedTripIds(Booking booking)
    {
        if (booking.TripId.HasValue)
        {
            yield return booking.TripId.Value;
        }

        if (booking.ReturnTripId.HasValue)
        {
            yield return booking.ReturnTripId.Value;
        }

        foreach (var tripId in booking.Passengers
            .Select(x => x.TripId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value))
        {
            yield return tripId;
        }
    }
}
