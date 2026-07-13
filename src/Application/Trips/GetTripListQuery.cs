using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

public sealed record TripAdminListItemDto(
    Guid TripId,
    string TripCode,
    string RouteCode,
    string RouteName,
    string RouteType,
    DateOnly OperatingDate,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    int CapacitySnapshot,
    string TripStatus,
    string? StatusNote,
    string TripType,
    Guid? SourceBookingId);

[Authorize(Roles = "Admin,Manager,Staff")]
public sealed record GetTripListQuery(
    DateOnly? OperatingDate,
    string? RouteCode,
    string? Status,
    string? TripType = null,
    string? RouteType = null) : IRequest<IReadOnlyList<TripAdminListItemDto>>;

public sealed class GetTripListQueryValidator : AbstractValidator<GetTripListQuery>
{
    public GetTripListQueryValidator()
    {
        RuleFor(x => x.TripType)
            .Must(x => string.Equals(x, TripTypes.Regular, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x, TripTypes.Charter, StringComparison.OrdinalIgnoreCase))
            .WithMessage($"tripType chi nhan {TripTypes.Regular} hoac {TripTypes.Charter}.")
            .When(x => !string.IsNullOrWhiteSpace(x.TripType));

        RuleFor(x => x.RouteType)
            .Must(RouteTypes.IsValid)
            .WithMessage($"routeType chi nhan {RouteTypes.Regular}, {RouteTypes.SightseeingLoop} hoac {RouteTypes.CharterReference}.")
            .When(x => !string.IsNullOrWhiteSpace(x.RouteType));
    }
}

public sealed class GetTripListQueryHandler : IRequestHandler<GetTripListQuery, IReadOnlyList<TripAdminListItemDto>>
{
    private readonly IApplicationDbContext _context;

    public GetTripListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<TripAdminListItemDto>> Handle(
        GetTripListQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Set<Trip>()
            .Include(t => t.Route)
            .AsQueryable();

        if (request.OperatingDate.HasValue)
            query = query.Where(t => t.OperatingDate == request.OperatingDate.Value);

        if (!string.IsNullOrWhiteSpace(request.RouteCode))
        {
            var rc = request.RouteCode.Trim().ToUpperInvariant();
            query = query.Where(t => t.Route.RouteCode == rc);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<TripStatus>(request.Status, ignoreCase: true, out var status))
            query = query.Where(t => t.TripStatus == status);

        if (!string.IsNullOrWhiteSpace(request.TripType))
        {
            var tripType = string.Equals(request.TripType, TripTypes.Charter, StringComparison.OrdinalIgnoreCase)
                ? TripTypes.Charter
                : TripTypes.Regular;
            query = query.Where(t => t.TripType == tripType);
        }

        if (!string.IsNullOrWhiteSpace(request.RouteType))
        {
            var routeType = RouteTypes.Normalize(request.RouteType);
            query = query.Where(t => t.Route.RouteType == routeType);
        }

        return await query
            .OrderByDescending(t => t.OperatingDate)
            .ThenBy(t => t.DepartureTime)
            .Select(t => new TripAdminListItemDto(
                t.Id, t.TripCode,
                t.Route.RouteCode, t.Route.RouteName, t.Route.RouteType,
                t.OperatingDate, t.DepartureTime, t.ArrivalTime,
                t.CapacitySnapshot, t.TripStatus.ToString(), t.StatusNote,
                t.TripType, t.SourceBookingId))
            .ToListAsync(cancellationToken);
    }
}
