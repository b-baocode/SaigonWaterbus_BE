using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Routes;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using ForbiddenAccessException = SaigonWaterbus.Application.Common.Exceptions.ForbiddenAccessException;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;
using static SaigonWaterbus.Application.CharterBookings.CharterRouteDrawRequestSupport;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CreateCharterRouteDrawRequestCommand(Guid BookingId, string? Notes = null)
    : IRequest<CharterRouteDrawRequestDetailDto>;

public sealed record GetCharterRouteDrawRequestListQuery(string? Status = null)
    : IRequest<IReadOnlyList<CharterRouteDrawRequestListItemDto>>;

public sealed record GetCharterRouteDrawRequestDetailQuery(Guid RequestId)
    : IRequest<CharterRouteDrawRequestDetailDto>;

public sealed record MarkCharterRouteDrawRequestInProgressCommand(Guid RequestId)
    : IRequest<CharterRouteDrawRequestDetailDto>;

public sealed record CompleteCharterRouteDrawRequestCommand(Guid RequestId, Guid RouteId)
    : IRequest<CharterRouteDrawRequestDetailDto>;

public sealed record CancelCharterRouteDrawRequestCommand(Guid RequestId)
    : IRequest;

public sealed record CharterRouteDrawRequestListItemDto(
    Guid RequestId,
    Guid BookingId,
    string BookingCode,
    string Status,
    DateOnly? DepartureDate,
    TimeOnly? StartTime,
    string ContactName,
    int PassengerCount,
    Guid? CandidateRouteId,
    Guid? ResultRouteId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? InProgressAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt);

public sealed record CharterRouteDrawRequestDetailDto(
    Guid RequestId,
    Guid BookingId,
    string BookingCode,
    string Status,
    DateOnly? DepartureDate,
    TimeOnly? StartTime,
    string ContactName,
    string ContactPhone,
    int PassengerCount,
    IReadOnlyList<CharterRouteDrawRequestStopDto> Stops,
    RouteDetailDto? CandidateRoute,
    IReadOnlyList<CharterBookingRouteCandidateLegDto> CandidateLegs,
    RouteDetailDto? ResultRoute,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? InProgressAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt);

public sealed record CharterRouteDrawRequestStopDto(
    Guid StationId,
    string StationCode,
    string StationName,
    int StopOrder,
    decimal? Latitude,
    decimal? Longitude,
    int StayDurationMinutes,
    string? Note);

public sealed class CreateCharterRouteDrawRequestCommandValidator
    : AbstractValidator<CreateCharterRouteDrawRequestCommand>
{
    public CreateCharterRouteDrawRequestCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class CompleteCharterRouteDrawRequestCommandValidator
    : AbstractValidator<CompleteCharterRouteDrawRequestCommand>
{
    public CompleteCharterRouteDrawRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.RouteId).NotEmpty();
    }
}

public sealed class CreateCharterRouteDrawRequestCommandHandler
    : IRequestHandler<CreateCharterRouteDrawRequestCommand, CharterRouteDrawRequestDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public CreateCharterRouteDrawRequestCommandHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CharterRouteDrawRequestDetailDto> Handle(
        CreateCharterRouteDrawRequestCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor))
        {
            throw new ForbiddenAccessException();
        }

        var existingOpenRequest = await LoadRequestDetailQuery(_context)
            .FirstOrDefaultAsync(x => x.BookingId == request.BookingId
                && (x.Status == CharterRouteDrawRequest.PendingStatus
                    || x.Status == CharterRouteDrawRequest.InProgressStatus),
                cancellationToken);
        if (existingOpenRequest is not null)
        {
            return await ToDetailDtoAsync(_context, existingOpenRequest, cancellationToken);
        }

        var booking = await CharterBookingRoutePlanSupport.LoadBookingWithItineraryAsync(
            _context,
            request.BookingId,
            cancellationToken);
        var currentCharterRoute = booking.CharterRouteId.HasValue
            ? await _context.Set<Route>()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == booking.CharterRouteId.Value, cancellationToken)
            : null;
        if (currentCharterRoute?.RouteGeometry is not null)
        {
            throw CreateValidation("bookingId", "Booking charter đã có route geometry, không cần gửi GPS vẽ tuyến.");
        }

        var stops = BuildStopDrafts(booking);
        EnsureValidStopDrafts(stops);
        var stationSequence = stops.Select(x => x.Station.Id).ToArray();
        var candidateRoute = await CharterBookingTripSupport.FindMatchingRouteAsync(
            _context,
            stationSequence,
            cancellationToken);
        if (candidateRoute?.RouteGeometry is null)
        {
            candidateRoute = null;
        }

        var drawRequest = new CharterRouteDrawRequest
        {
            BookingId = booking.Id,
            Booking = booking,
            Status = CharterRouteDrawRequest.PendingStatus,
            CandidateRouteId = candidateRoute?.Id,
            RequestedByUserId = actor.Id,
            Notes = NormalizeOptionalText(request.Notes, 1000),
            Stops = stops.Select(x => new CharterRouteDrawRequestStop
            {
                StationId = x.Station.Id,
                Station = x.Station,
                StopOrder = x.StopOrder,
                StationCode = x.Station.StationCode,
                StationName = x.Station.StationName,
                Latitude = x.Station.Latitude,
                Longitude = x.Station.Longitude,
                StayDurationMinutes = x.StayDurationMinutes,
                Note = NormalizeOptionalText(x.Note, 500)
            }).ToList()
        };

        _context.Set<CharterRouteDrawRequest>().Add(drawRequest);
        await _context.SaveChangesAsync(cancellationToken);

        var savedRequest = await LoadRequestDetailQuery(_context)
            .SingleAsync(x => x.Id == drawRequest.Id, cancellationToken);
        return await ToDetailDtoAsync(_context, savedRequest, cancellationToken);
    }
}

public sealed class GetCharterRouteDrawRequestListQueryHandler
    : IRequestHandler<GetCharterRouteDrawRequestListQuery, IReadOnlyList<CharterRouteDrawRequestListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCharterRouteDrawRequestListQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<CharterRouteDrawRequestListItemDto>> Handle(
        GetCharterRouteDrawRequestListQuery request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var status = NormalizeStatusFilter(request.Status);
        var query = _context.Set<CharterRouteDrawRequest>()
            .AsNoTracking()
            .Include(x => x.Booking)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        return await query
            .OrderBy(x => x.Status == CharterRouteDrawRequest.PendingStatus ? 0 : 1)
            .ThenByDescending(x => x.Created)
            .Select(x => new CharterRouteDrawRequestListItemDto(
                x.Id,
                x.BookingId,
                x.Booking.BookingCode,
                x.Status,
                x.Booking.DepartureDate,
                x.Booking.StartTime,
                x.Booking.ContactName,
                x.Booking.PassengerCount ?? 0,
                x.CandidateRouteId,
                x.ResultRouteId,
                x.Created,
                x.InProgressAt,
                x.CompletedAt,
                x.CancelledAt))
            .ToListAsync(cancellationToken);
    }
}

public sealed class GetCharterRouteDrawRequestDetailQueryHandler
    : IRequestHandler<GetCharterRouteDrawRequestDetailQuery, CharterRouteDrawRequestDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCharterRouteDrawRequestDetailQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CharterRouteDrawRequestDetailDto> Handle(
        GetCharterRouteDrawRequestDetailQuery request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var drawRequest = await LoadRequestDetailQuery(_context)
            .SingleOrDefaultAsync(x => x.Id == request.RequestId, cancellationToken)
            ?? throw new NotFoundException("Charter route draw request not found.");

        return await ToDetailDtoAsync(_context, drawRequest, cancellationToken);
    }
}

public sealed class MarkCharterRouteDrawRequestInProgressCommandHandler
    : IRequestHandler<MarkCharterRouteDrawRequestInProgressCommand, CharterRouteDrawRequestDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public MarkCharterRouteDrawRequestInProgressCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CharterRouteDrawRequestDetailDto> Handle(
        MarkCharterRouteDrawRequestInProgressCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor))
        {
            throw new ForbiddenAccessException();
        }

        var drawRequest = await LoadRequestDetailQuery(_context)
            .SingleOrDefaultAsync(x => x.Id == request.RequestId, cancellationToken)
            ?? throw new NotFoundException("Charter route draw request not found.");

        if (drawRequest.Status == CharterRouteDrawRequest.DoneStatus)
        {
            return await ToDetailDtoAsync(_context, drawRequest, cancellationToken);
        }

        if (drawRequest.Status == CharterRouteDrawRequest.CancelledStatus)
        {
            throw CreateValidation("status", "Yêu cầu vẽ tuyến đã bị hủy.");
        }

        if (drawRequest.Status == CharterRouteDrawRequest.PendingStatus)
        {
            drawRequest.Status = CharterRouteDrawRequest.InProgressStatus;
            drawRequest.InProgressByUserId = actor.Id;
            drawRequest.InProgressAt = _timeProvider.GetUtcNow();
            await _context.SaveChangesAsync(cancellationToken);
        }

        return await ToDetailDtoAsync(_context, drawRequest, cancellationToken);
    }
}

public sealed class CompleteCharterRouteDrawRequestCommandHandler
    : IRequestHandler<CompleteCharterRouteDrawRequestCommand, CharterRouteDrawRequestDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CompleteCharterRouteDrawRequestCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CharterRouteDrawRequestDetailDto> Handle(
        CompleteCharterRouteDrawRequestCommand request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        return await _context.ExecuteInTransactionAsync(async ct =>
        {
            var drawRequest = await LoadRequestDetailQuery(_context)
                .SingleOrDefaultAsync(x => x.Id == request.RequestId, ct)
                ?? throw new NotFoundException("Charter route draw request not found.");

            if (drawRequest.Status == CharterRouteDrawRequest.DoneStatus)
            {
                if (drawRequest.ResultRouteId == request.RouteId
                    && drawRequest.Booking.CharterRouteId == request.RouteId)
                {
                    return await ToDetailDtoAsync(_context, drawRequest, ct);
                }

                throw CreateValidation("routeId", "Yêu cầu vẽ tuyến đã hoàn tất với route khác.");
            }

            if (drawRequest.Status == CharterRouteDrawRequest.CancelledStatus)
            {
                throw CreateValidation("status", "Yêu cầu vẽ tuyến đã bị hủy.");
            }

            var route = await _context.Set<Route>()
                .Include(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
                .SingleOrDefaultAsync(x => x.Id == request.RouteId, ct)
                ?? throw new NotFoundException("Route not found.");
            EnsureRouteCanCompleteRequest(route, drawRequest);

            if (drawRequest.Booking.CharterRouteId.HasValue
                && drawRequest.Booking.CharterRouteId.Value != route.Id
                && drawRequest.Booking.CharterRoute?.RouteGeometry is not null)
            {
                throw CreateValidation("bookingId", "Booking charter đã có route geometry khác.");
            }

            var now = _timeProvider.GetUtcNow();
            drawRequest.Status = CharterRouteDrawRequest.DoneStatus;
            drawRequest.ResultRouteId = route.Id;
            drawRequest.CompletedAt = now;
            drawRequest.Booking.CharterRouteId = route.Id;
            drawRequest.Booking.CharterRoute = route;

            await _context.SaveChangesAsync(ct);

            return await ToDetailDtoAsync(_context, drawRequest, ct);
        }, cancellationToken);
    }
}

public sealed class CancelCharterRouteDrawRequestCommandHandler
    : IRequestHandler<CancelCharterRouteDrawRequestCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CancelCharterRouteDrawRequestCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task Handle(CancelCharterRouteDrawRequestCommand request, CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var drawRequest = await _context.Set<CharterRouteDrawRequest>()
            .SingleOrDefaultAsync(x => x.Id == request.RequestId, cancellationToken)
            ?? throw new NotFoundException("Charter route draw request not found.");

        if (drawRequest.Status == CharterRouteDrawRequest.DoneStatus)
        {
            throw CreateValidation("status", "Yêu cầu vẽ tuyến đã hoàn tất, không thể hủy.");
        }

        if (drawRequest.Status != CharterRouteDrawRequest.CancelledStatus)
        {
            drawRequest.Status = CharterRouteDrawRequest.CancelledStatus;
            drawRequest.CancelledAt = _timeProvider.GetUtcNow();
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}

internal static class CharterRouteDrawRequestSupport
{
    public static IQueryable<CharterRouteDrawRequest> LoadRequestDetailQuery(IApplicationDbContext context) =>
        context.Set<CharterRouteDrawRequest>()
            .Include(x => x.Booking)
                .ThenInclude(x => x.CharterRoute)
            .Include(x => x.Stops)
                .ThenInclude(x => x.Station)
            .Include(x => x.CandidateRoute)
                .ThenInclude(x => x!.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.ResultRoute)
                .ThenInclude(x => x!.RouteStops)
                    .ThenInclude(x => x.Station);

    public static async Task<CharterRouteDrawRequestDetailDto> ToDetailDtoAsync(
        IApplicationDbContext context,
        CharterRouteDrawRequest request,
        CancellationToken cancellationToken)
    {
        var stops = request.Stops
            .OrderBy(x => x.StopOrder)
            .Select(x => new CharterRouteDrawRequestStopDto(
                x.StationId,
                x.StationCode,
                x.StationName,
                x.StopOrder,
                x.Latitude,
                x.Longitude,
                x.StayDurationMinutes,
                x.Note))
            .ToList();

        var candidateLegs = await LoadCandidateLegsAsync(context, request, cancellationToken);

        return new CharterRouteDrawRequestDetailDto(
            request.Id,
            request.BookingId,
            request.Booking.BookingCode,
            request.Status,
            request.Booking.DepartureDate,
            request.Booking.StartTime,
            request.Booking.ContactName,
            request.Booking.ContactPhone,
            request.Booking.PassengerCount ?? 0,
            stops,
            ToRouteDetailDto(request.CandidateRoute),
            candidateLegs,
            ToRouteDetailDto(request.ResultRoute),
            request.Notes,
            request.Created,
            request.InProgressAt,
            request.CompletedAt,
            request.CancelledAt);
    }

    public static IReadOnlyList<CharterRouteDrawRequestStopDraft> BuildStopDrafts(Booking booking)
    {
        var stops = new List<CharterRouteDrawRequestStopDraft>();
        if (booking.FromStation is not null)
        {
            stops.Add(new CharterRouteDrawRequestStopDraft(booking.FromStation, 1, 0, null));
        }

        foreach (var stop in booking.ItineraryStops.OrderBy(x => x.StopOrder))
        {
            if (stop.Station is not null)
            {
                stops.Add(new CharterRouteDrawRequestStopDraft(
                    stop.Station,
                    stops.Count + 1,
                    stop.StayDurationMinutes,
                    stop.Note));
            }
        }

        if (booking.ToStation is not null)
        {
            stops.Add(new CharterRouteDrawRequestStopDraft(booking.ToStation, stops.Count + 1, 0, null));
        }

        return stops;
    }

    public static void EnsureValidStopDrafts(IReadOnlyList<CharterRouteDrawRequestStopDraft> stops)
    {
        if (stops.Count < 2)
        {
            throw CreateValidation("bookingId", "Lộ trình charter cần ít nhất bến đi và một bến đến.");
        }

        for (var i = 1; i < stops.Count; i++)
        {
            if (stops[i].Station.Id == stops[i - 1].Station.Id)
            {
                throw CreateValidation(
                    "bookingId",
                    $"Lộ trình có hai bến liên tiếp trùng '{stops[i].Station.StationName}'.");
            }
        }
    }

    public static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    public static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalized = status.Trim();
        if (string.Equals(normalized, CharterRouteDrawRequest.PendingStatus, StringComparison.OrdinalIgnoreCase))
        {
            return CharterRouteDrawRequest.PendingStatus;
        }

        if (string.Equals(normalized, CharterRouteDrawRequest.InProgressStatus, StringComparison.OrdinalIgnoreCase))
        {
            return CharterRouteDrawRequest.InProgressStatus;
        }

        if (string.Equals(normalized, CharterRouteDrawRequest.DoneStatus, StringComparison.OrdinalIgnoreCase))
        {
            return CharterRouteDrawRequest.DoneStatus;
        }

        if (string.Equals(normalized, CharterRouteDrawRequest.CancelledStatus, StringComparison.OrdinalIgnoreCase))
        {
            return CharterRouteDrawRequest.CancelledStatus;
        }

        throw CreateValidation("status", "Status hợp lệ: Pending | InProgress | Done | Cancelled.");
    }

    public static void EnsureRouteCanCompleteRequest(Route route, CharterRouteDrawRequest request)
    {
        if (route.Status != "Active")
        {
            throw CreateValidation("routeId", "Route phải đang Active.");
        }

        if (route.RouteGeometry is null || route.RouteGeometry.NumPoints < 2)
        {
            throw CreateValidation("routeId", "Route phải có routeGeometry hợp lệ trước khi complete.");
        }

        var requestStationIds = request.Stops
            .OrderBy(x => x.StopOrder)
            .Select(x => x.StationId)
            .ToArray();
        var routeStationIds = route.RouteStops
            .OrderBy(x => x.StopOrder)
            .Select(x => x.StationId)
            .ToArray();
        if (!routeStationIds.SequenceEqual(requestStationIds))
        {
            throw CreateValidation("routeId", "Route phải có stops khớp đúng thứ tự bến của yêu cầu vẽ tuyến.");
        }
    }

    public static RouteDetailDto? ToRouteDetailDto(Route? route)
    {
        if (route is null)
        {
            return null;
        }

        var stops = route.RouteStops
            .OrderBy(x => x.StopOrder)
            .Select(x => new RouteStopDto(
                x.Id,
                x.StationId,
                x.Station.StationCode,
                x.Station.StationName,
                x.StopOrder,
                x.StandardTravelMin,
                x.DistanceFromPreviousKm,
                x.IsPickupAllowed,
                x.IsDropoffAllowed))
            .ToList();

        return new RouteDetailDto(
            route.Id,
            route.RouteCode,
            route.RouteName,
            route.RouteType,
            route.Description,
            route.BaseDistanceKm,
            route.EstimatedDurationMin,
            route.Status,
            stops,
            route.RouteGeometry is null
                ? null
                : route.RouteGeometry.Coordinates.Select(x => new double[] { x.X, x.Y }).ToList(),
            RoutePresentationSupport.ResolveLabel(route.RouteType),
            RoutePresentationSupport.IsSelectableForCharterQuote(route),
            RoutePresentationSupport.IsGeneratedForBooking(route));
    }

    private static async Task<IReadOnlyList<CharterBookingRouteCandidateLegDto>> LoadCandidateLegsAsync(
        IApplicationDbContext context,
        CharterRouteDrawRequest request,
        CancellationToken cancellationToken)
    {
        var booking = request.Booking;
        if (booking.FromStation is null || booking.ToStation is null)
        {
            booking = await CharterBookingRoutePlanSupport.LoadBookingWithItineraryAsync(
                context,
                request.BookingId,
                cancellationToken);
        }

        var candidates = await CharterBookingRoutePlanSupport.GetCandidatesAsync(
            context,
            booking,
            cancellationToken);
        return candidates.Legs;
    }

    public static ValidationException CreateValidation(string propertyName, string message) =>
        new([new ValidationFailure(propertyName, message)]);

    public sealed record CharterRouteDrawRequestStopDraft(
        Station Station,
        int StopOrder,
        int StayDurationMinutes,
        string? Note);
}
