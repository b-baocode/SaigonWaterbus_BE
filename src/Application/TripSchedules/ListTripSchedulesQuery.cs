using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.TripSchedules;

public sealed record ListTripSchedulesQuery(bool? IsActive = null) : IRequest<List<TripScheduleDto>>;

public sealed class ListTripSchedulesQueryHandler : IRequestHandler<ListTripSchedulesQuery, List<TripScheduleDto>>
{
    private readonly IApplicationDbContext _context;

    public ListTripSchedulesQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<List<TripScheduleDto>> Handle(ListTripSchedulesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Set<TripSchedule>()
            .Include(s => s.Route)
            .Include(s => s.Vessel)
            .AsQueryable();

        if (request.IsActive.HasValue)
            query = query.Where(s => s.IsActive == request.IsActive.Value);

        var schedules = await query
            .OrderBy(s => s.DepartureTime)
            .ToListAsync(cancellationToken);

        return schedules.Select(s => new TripScheduleDto(
            s.Id,
            s.RouteId, s.Route.RouteName, s.Route.RouteCode,
            s.VesselId, s.Vessel.Name,
            s.DepartureTime,
            s.RecurrenceType,
            s.WeeklyDays,
            s.MonthlyDay,
            s.HorizonDays,
            s.ValidFrom, s.ValidTo,
            s.IsActive)).ToList();
    }
}
