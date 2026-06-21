using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.PublicBoard;

public sealed record PublicDepartureBoardItemDto(
    Guid TripId,
    string TripCode,
    Guid RouteId,
    string RouteCode,
    string RouteName,
    Guid StationId,
    string StationCode,
    string StationName,
    int StopOrder,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? ActualArrival,
    DateTimeOffset? ActualDeparture,
    string TripStatus,
    string StopStatus,
    string DisplayStatus,
    int? MinutesToArrival,
    int? MinutesToDeparture);

public sealed record GetPublicDepartureBoardQuery(
    Guid? StationId = null,
    string? StationCode = null,
    int LookAheadMinutes = 180,
    int IncludeDepartedMinutes = 20,
    int BoardingLeadMinutes = 10,
    int ApproachingLeadMinutes = 10) : IRequest<IReadOnlyList<PublicDepartureBoardItemDto>>;

public sealed class GetPublicDepartureBoardQueryHandler
    : IRequestHandler<GetPublicDepartureBoardQuery, IReadOnlyList<PublicDepartureBoardItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public GetPublicDepartureBoardQueryHandler(
        IApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PublicDepartureBoardItemDto>> Handle(
        GetPublicDepartureBoardQuery request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var lookAheadMinutes = Clamp(request.LookAheadMinutes, 1, 1440);
        var includeDepartedMinutes = Clamp(request.IncludeDepartedMinutes, 0, 240);
        var boardingLeadMinutes = Clamp(request.BoardingLeadMinutes, 0, 120);
        var approachingLeadMinutes = Clamp(request.ApproachingLeadMinutes, 0, 120);
        var from = now.AddMinutes(-includeDepartedMinutes);
        var to = now.AddMinutes(lookAheadMinutes);
        var stationCode = string.IsNullOrWhiteSpace(request.StationCode)
            ? null
            : request.StationCode.Trim().ToUpperInvariant();

        var query = _context.Set<TripStop>()
            .AsNoTracking()
            .Include(x => x.Trip)
                .ThenInclude(x => x.Route)
            .Include(x => x.RouteStop)
                .ThenInclude(x => x.Station)
            .Where(x => x.Trip.TripStatus != TripStatus.Cancelled);

        if (request.StationId.HasValue)
        {
            query = query.Where(x => x.RouteStop.StationId == request.StationId.Value);
        }

        if (stationCode is not null)
        {
            query = query.Where(x => x.RouteStop.Station.StationCode == stationCode);
        }

        var stops = await query
            .Where(x =>
                (x.ScheduledArrival.HasValue
                    && x.ScheduledArrival.Value >= from
                    && x.ScheduledArrival.Value <= to)
                || (x.ScheduledDeparture.HasValue
                    && x.ScheduledDeparture.Value >= from
                    && x.ScheduledDeparture.Value <= to)
                || (x.ScheduledArrival.HasValue
                    && x.ScheduledDeparture.HasValue
                    && x.ScheduledArrival.Value <= now
                    && x.ScheduledDeparture.Value >= now))
            .OrderBy(x => x.ScheduledDeparture ?? x.ScheduledArrival)
            .ThenBy(x => x.Trip.TripCode)
            .ThenBy(x => x.StopOrder)
            .ToArrayAsync(cancellationToken);

        return stops
            .Select(x => ToDto(x, now, boardingLeadMinutes, approachingLeadMinutes))
            .Where(x => x.DisplayStatus != PublicBoardStatuses.Hidden)
            .ToArray();
    }

    private static PublicDepartureBoardItemDto ToDto(
        TripStop stop,
        DateTimeOffset now,
        int boardingLeadMinutes,
        int approachingLeadMinutes)
    {
        var arrival = stop.ActualArrival ?? stop.ScheduledArrival;
        var departure = stop.ActualDeparture ?? stop.ScheduledDeparture;
        var displayStatus = ResolveDisplayStatus(
            stop,
            arrival,
            departure,
            now,
            boardingLeadMinutes,
            approachingLeadMinutes);

        return new PublicDepartureBoardItemDto(
            stop.TripId,
            stop.Trip.TripCode,
            stop.Trip.RouteId,
            stop.Trip.Route.RouteCode,
            stop.Trip.Route.RouteName,
            stop.RouteStop.StationId,
            stop.RouteStop.Station.StationCode,
            stop.RouteStop.Station.StationName,
            stop.StopOrder,
            stop.ScheduledArrival,
            stop.ScheduledDeparture,
            stop.ActualArrival,
            stop.ActualDeparture,
            stop.Trip.TripStatus.ToString(),
            stop.StopStatus,
            displayStatus,
            MinutesUntil(now, arrival),
            MinutesUntil(now, departure));
    }

    private static string ResolveDisplayStatus(
        TripStop stop,
        DateTimeOffset? arrival,
        DateTimeOffset? departure,
        DateTimeOffset now,
        int boardingLeadMinutes,
        int approachingLeadMinutes)
    {
        if (stop.Trip.TripStatus == TripStatus.Completed)
        {
            return PublicBoardStatuses.Arrived;
        }

        if (arrival.HasValue && stop.StopOrder > 1 && now < arrival.Value)
        {
            var minutesToArrival = (arrival.Value - now).TotalMinutes;
            if (minutesToArrival <= approachingLeadMinutes)
            {
                return PublicBoardStatuses.ArrivingSoon;
            }

            return PublicBoardStatuses.Upcoming;
        }

        if (departure.HasValue
            && now >= departure.Value.AddMinutes(-boardingLeadMinutes)
            && now < departure.Value)
        {
            return PublicBoardStatuses.Boarding;
        }

        if (arrival.HasValue && now < arrival.Value)
        {
            return PublicBoardStatuses.Upcoming;
        }

        if (arrival.HasValue && departure.HasValue && now >= arrival.Value && now < departure.Value)
        {
            return PublicBoardStatuses.Boarding;
        }

        if (departure.HasValue && now >= departure.Value)
        {
            return PublicBoardStatuses.Departed;
        }

        if (arrival.HasValue && now >= arrival.Value)
        {
            return PublicBoardStatuses.Arrived;
        }

        return PublicBoardStatuses.Upcoming;
    }

    private static int? MinutesUntil(DateTimeOffset now, DateTimeOffset? target)
    {
        if (!target.HasValue)
        {
            return null;
        }

        return Math.Max(0, (int)Math.Ceiling((target.Value - now).TotalMinutes));
    }

    private static int Clamp(int value, int min, int max) =>
        Math.Min(max, Math.Max(min, value));
}

internal static class PublicBoardStatuses
{
    public const string Upcoming = "Upcoming";
    public const string Boarding = "Boarding";
    public const string ArrivingSoon = "ArrivingSoon";
    public const string Departed = "Departed";
    public const string Arrived = "Arrived";
    public const string Hidden = "Hidden";
}
