using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.TripSchedules;

public sealed record UpdateTripScheduleCommand(
    Guid Id,
    int HorizonDays,
    DateOnly? ValidTo,
    bool IsActive) : IRequest<TripScheduleDto>;

public sealed class UpdateTripScheduleCommandValidator : AbstractValidator<UpdateTripScheduleCommand>
{
    public UpdateTripScheduleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.HorizonDays).InclusiveBetween(1, 365);
    }
}

public sealed class UpdateTripScheduleCommandHandler : IRequestHandler<UpdateTripScheduleCommand, TripScheduleDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateTripScheduleCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<TripScheduleDto> Handle(UpdateTripScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await _context.Set<TripSchedule>()
            .Include(s => s.Route)
            .Include(s => s.Vessel)
            .SingleOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException($"TripSchedule '{request.Id}' not found.");

        schedule.HorizonDays = request.HorizonDays;
        schedule.ValidTo = request.ValidTo;
        schedule.IsActive = request.IsActive;

        await _context.SaveChangesAsync(cancellationToken);

        return new TripScheduleDto(
            schedule.Id,
            schedule.RouteId, schedule.Route.RouteName, schedule.Route.RouteCode,
            schedule.VesselId, schedule.Vessel.Name,
            schedule.DepartureTime,
            schedule.RecurrenceType,
            schedule.WeeklyDays,
            schedule.MonthlyDay,
            schedule.HorizonDays,
            schedule.ValidFrom, schedule.ValidTo,
            schedule.IsActive);
    }
}
