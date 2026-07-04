using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

public sealed record TripAdminListItemDto(
    Guid TripId,
    string TripCode,
    string RouteCode,
    string RouteName,
    DateOnly OperatingDate,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    int CapacitySnapshot,
    string TripStatus,
    string? StatusNote);

[Authorize(Roles = "Admin,Manager")]
public sealed record GetTripListQuery(
    DateOnly? OperatingDate,
    string? RouteCode,
    string? Status) : IRequest<IReadOnlyList<TripAdminListItemDto>>;

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

        return await query
            .OrderByDescending(t => t.OperatingDate)
            .ThenBy(t => t.DepartureTime)
            .Select(t => new TripAdminListItemDto(
                t.Id, t.TripCode,
                t.Route.RouteCode, t.Route.RouteName,
                t.OperatingDate, t.DepartureTime, t.ArrivalTime,
                t.CapacitySnapshot, t.TripStatus.ToString(), t.StatusNote))
            .ToListAsync(cancellationToken);
    }
}
