using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.TripSchedules;

public sealed record CreateTripScheduleCommand(
    string RouteCode,
    Guid VesselId,
    TimeOnly DepartureTime,
    RecurrenceType RecurrenceType,
    string? WeeklyDays,
    int? MonthlyDay,
    int HorizonDays,
    DateOnly ValidFrom,
    DateOnly? ValidTo) : IRequest<TripScheduleDto>;

public sealed class CreateTripScheduleCommandValidator : AbstractValidator<CreateTripScheduleCommand>
{
    public CreateTripScheduleCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.VesselId).NotEmpty();
        RuleFor(x => x.HorizonDays).InclusiveBetween(1, 365);
        RuleFor(x => x.ValidFrom).NotEmpty();

        RuleFor(x => x.ValidTo)
            .GreaterThanOrEqualTo(x => x.ValidFrom)
            .When(x => x.ValidTo.HasValue)
            .WithMessage("ValidTo phải lớn hơn hoặc bằng ValidFrom.");

        RuleFor(x => x.WeeklyDays)
            .NotEmpty()
            .When(x => x.RecurrenceType == RecurrenceType.Weekly)
            .WithMessage("WeeklyDays bắt buộc khi RecurrenceType là Weekly.");

        RuleFor(x => x.MonthlyDay)
            .NotNull()
            .InclusiveBetween(1, 31)
            .When(x => x.RecurrenceType == RecurrenceType.Monthly)
            .WithMessage("MonthlyDay (1-31) bắt buộc khi RecurrenceType là Monthly.");
    }
}

public sealed class CreateTripScheduleCommandHandler : IRequestHandler<CreateTripScheduleCommand, TripScheduleDto>
{
    private readonly IApplicationDbContext _context;

    public CreateTripScheduleCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<TripScheduleDto> Handle(CreateTripScheduleCommand request, CancellationToken cancellationToken)
    {
        var route = await _context.Set<Route>()
            .SingleOrDefaultAsync(r => r.RouteCode == request.RouteCode.Trim().ToUpperInvariant() && r.Status == "Active", cancellationToken)
            ?? throw new NotFoundException($"Route '{request.RouteCode}' not found or inactive.");

        var vessel = await _context.Set<Vessel>()
            .SingleOrDefaultAsync(v => v.Id == request.VesselId && v.Status == VesselStatus.Active, cancellationToken)
            ?? throw new NotFoundException($"Vessel '{request.VesselId}' not found or inactive.");

        if (!vessel.SeatsConfigured)
            throw new ValidationException([new ValidationFailure("VesselId", "Tàu chưa được cấu hình ghế. Vui lòng cấu hình ghế trước khi tạo lịch.")]);

        var schedule = new TripSchedule
        {
            RouteId = route.Id,
            VesselId = vessel.Id,
            DepartureTime = request.DepartureTime,
            RecurrenceType = request.RecurrenceType,
            WeeklyDays = request.WeeklyDays?.Trim(),
            MonthlyDay = request.MonthlyDay,
            HorizonDays = request.HorizonDays,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            IsActive = true
        };

        _context.Set<TripSchedule>().Add(schedule);
        await _context.SaveChangesAsync(cancellationToken);

        return new TripScheduleDto(
            schedule.Id,
            route.Id, route.RouteName, route.RouteCode,
            vessel.Id, vessel.Name,
            schedule.DepartureTime,
            schedule.RecurrenceType,
            schedule.WeeklyDays,
            schedule.MonthlyDay,
            schedule.HorizonDays,
            schedule.ValidFrom, schedule.ValidTo,
            schedule.IsActive);
    }
}
