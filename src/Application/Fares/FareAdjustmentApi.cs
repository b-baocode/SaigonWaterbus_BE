using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Fares;

public sealed record FareAdjustmentDto(
    Guid FareAdjustmentId,
    string Scope,
    DateOnly? Date,
    string Name,
    decimal SurchargePercent,
    decimal Multiplier,
    decimal RoundingStep,
    bool IsActive);

public sealed record EffectiveFareAdjustmentDto(
    DateOnly Date,
    string Scope,
    string Name,
    decimal SurchargePercent,
    decimal Multiplier,
    decimal RoundingStep);

public static class FareAdjustmentSupport
{
    private const decimal DefaultRoundingStep = 1000m;

    public static decimal Multiplier(decimal surchargePercent) => (100m + surchargePercent) / 100m;

    public static decimal ApplySurcharge(decimal price, EffectiveFareAdjustmentDto? adjustment)
    {
        if (adjustment is null || adjustment.SurchargePercent == 0)
        {
            return price;
        }

        var adjusted = price * adjustment.Multiplier;
        return RoundUp(adjusted, adjustment.RoundingStep);
    }

    public static decimal RoundUp(decimal amount, decimal roundingStep)
    {
        if (roundingStep <= 1)
        {
            return decimal.Ceiling(amount);
        }

        return decimal.Ceiling(amount / roundingStep) * roundingStep;
    }

    public static async Task<EffectiveFareAdjustmentDto?> GetEffectiveAdjustmentAsync(
        IApplicationDbContext context,
        DateOnly operatingDate,
        CancellationToken cancellationToken)
    {
        var dateAdjustments = await context.Set<FareAdjustment>()
            .AsNoTracking()
            .Where(x => x.IsActive && x.Date == operatingDate)
            .ToListAsync(cancellationToken);
        var dateAdjustment = dateAdjustments
            .OrderByDescending(x => Priority(x.Scope))
            .ThenByDescending(x => x.Created)
            .FirstOrDefault();

        if (dateAdjustment is not null)
        {
            return ToEffectiveDto(operatingDate, dateAdjustment);
        }

        if (operatingDate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            return null;
        }

        var weekendAdjustment = await context.Set<FareAdjustment>()
            .AsNoTracking()
            .Where(x => x.IsActive
                     && x.Date == null
                     && x.Scope == FareAdjustmentScopes.Weekend)
            .OrderByDescending(x => x.Created)
            .FirstOrDefaultAsync(cancellationToken);

        return weekendAdjustment is null ? null : ToEffectiveDto(operatingDate, weekendAdjustment);
    }

    public static async Task<IReadOnlyDictionary<DateOnly, EffectiveFareAdjustmentDto?>> GetEffectiveAdjustmentsAsync(
        IApplicationDbContext context,
        IReadOnlyCollection<DateOnly> operatingDates,
        CancellationToken cancellationToken)
    {
        if (operatingDates.Count == 0)
        {
            return new Dictionary<DateOnly, EffectiveFareAdjustmentDto?>();
        }

        var distinctDates = operatingDates.Distinct().ToArray();
        var specificAdjustments = await context.Set<FareAdjustment>()
            .AsNoTracking()
            .Where(x => x.IsActive && x.Date.HasValue && distinctDates.Contains(x.Date.Value))
            .ToListAsync(cancellationToken);
        var specificByDate = specificAdjustments
            .GroupBy(x => x.Date!.Value)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => Priority(x.Scope))
                    .ThenByDescending(x => x.Created)
                    .First());

        var hasWeekendDate = distinctDates.Any(x => x.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        var weekendAdjustment = hasWeekendDate
            ? await context.Set<FareAdjustment>()
                .AsNoTracking()
                .Where(x => x.IsActive
                         && x.Date == null
                         && x.Scope == FareAdjustmentScopes.Weekend)
                .OrderByDescending(x => x.Created)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return distinctDates.ToDictionary(
            x => x,
            x =>
            {
                if (specificByDate.TryGetValue(x, out var dateAdjustment))
                {
                    return ToEffectiveDto(x, dateAdjustment);
                }

                return weekendAdjustment is not null
                    && x.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                    ? ToEffectiveDto(x, weekendAdjustment)
                    : null;
            });
    }

    public static FareAdjustmentDto ToDto(FareAdjustment adjustment) =>
        new(
            adjustment.Id,
            adjustment.Scope,
            adjustment.Date,
            adjustment.Name,
            adjustment.SurchargePercent,
            Multiplier(adjustment.SurchargePercent),
            adjustment.RoundingStep,
            adjustment.IsActive);

    private static EffectiveFareAdjustmentDto ToEffectiveDto(DateOnly operatingDate, FareAdjustment adjustment) =>
        new(
            operatingDate,
            adjustment.Scope,
            adjustment.Name,
            adjustment.SurchargePercent,
            Multiplier(adjustment.SurchargePercent),
            adjustment.RoundingStep);

    private static int Priority(string scope) =>
        scope switch
        {
            FareAdjustmentScopes.Special => 3,
            FareAdjustmentScopes.Holiday => 2,
            FareAdjustmentScopes.Weekend => 1,
            _ => 0
        };
}

public sealed record GetFareAdjustmentsQuery(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null) : IRequest<IReadOnlyList<FareAdjustmentDto>>;

public sealed class GetFareAdjustmentsQueryHandler
    : IRequestHandler<GetFareAdjustmentsQuery, IReadOnlyList<FareAdjustmentDto>>
{
    private readonly IApplicationDbContext _context;

    public GetFareAdjustmentsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<FareAdjustmentDto>> Handle(
        GetFareAdjustmentsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Set<FareAdjustment>().AsNoTracking();
        if (request.FromDate.HasValue)
        {
            query = query.Where(x => x.Date == null || x.Date >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(x => x.Date == null || x.Date <= request.ToDate.Value);
        }

        var rows = await query
            .OrderBy(x => x.Date.HasValue ? 1 : 0)
            .ThenBy(x => x.Date)
            .ThenBy(x => x.Scope)
            .ToListAsync(cancellationToken);

        return rows.Select(FareAdjustmentSupport.ToDto).ToList();
    }
}

public sealed record GetEffectiveFareAdjustmentQuery(DateOnly Date) : IRequest<EffectiveFareAdjustmentDto?>;

public sealed class GetEffectiveFareAdjustmentQueryHandler
    : IRequestHandler<GetEffectiveFareAdjustmentQuery, EffectiveFareAdjustmentDto?>
{
    private readonly IApplicationDbContext _context;

    public GetEffectiveFareAdjustmentQueryHandler(IApplicationDbContext context) => _context = context;

    public Task<EffectiveFareAdjustmentDto?> Handle(
        GetEffectiveFareAdjustmentQuery request,
        CancellationToken cancellationToken) =>
        FareAdjustmentSupport.GetEffectiveAdjustmentAsync(_context, request.Date, cancellationToken);
}

[Authorize(Roles = "Admin,Manager")]
public sealed record UpsertWeekendFareAdjustmentCommand(
    decimal SurchargePercent,
    decimal RoundingStep = 1000m,
    bool IsActive = true) : IRequest<FareAdjustmentDto>;

public sealed class UpsertWeekendFareAdjustmentCommandValidator
    : AbstractValidator<UpsertWeekendFareAdjustmentCommand>
{
    public UpsertWeekendFareAdjustmentCommandValidator()
    {
        FareAdjustmentValidationRules.Apply(
            RuleFor(x => x.SurchargePercent),
            RuleFor(x => x.RoundingStep));
    }
}

public sealed class UpsertWeekendFareAdjustmentCommandHandler
    : IRequestHandler<UpsertWeekendFareAdjustmentCommand, FareAdjustmentDto>
{
    private readonly IApplicationDbContext _context;

    public UpsertWeekendFareAdjustmentCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<FareAdjustmentDto> Handle(
        UpsertWeekendFareAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        var adjustment = await _context.Set<FareAdjustment>()
            .SingleOrDefaultAsync(
                x => x.Scope == FareAdjustmentScopes.Weekend && x.Date == null,
                cancellationToken);

        if (adjustment is null)
        {
            adjustment = new FareAdjustment
            {
                Scope = FareAdjustmentScopes.Weekend,
                Name = "Weekend"
            };
            _context.Set<FareAdjustment>().Add(adjustment);
        }

        adjustment.SurchargePercent = request.SurchargePercent;
        adjustment.RoundingStep = request.RoundingStep;
        adjustment.IsActive = request.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
        return FareAdjustmentSupport.ToDto(adjustment);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record UpsertFareCalendarDayCommand(
    DateOnly Date,
    string Scope,
    decimal SurchargePercent,
    string? Name = null,
    decimal RoundingStep = 1000m,
    bool IsActive = true) : IRequest<FareAdjustmentDto>;

public sealed class UpsertFareCalendarDayCommandValidator
    : AbstractValidator<UpsertFareCalendarDayCommand>
{
    public UpsertFareCalendarDayCommandValidator()
    {
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.Scope)
            .NotEmpty()
            .Must(scope =>
            {
                var normalized = FareAdjustmentScopes.Normalize(scope);
                return normalized is FareAdjustmentScopes.Holiday or FareAdjustmentScopes.Special;
            })
            .WithMessage("scope chỉ nhận Holiday hoặc Special cho ngày cụ thể.");
        RuleFor(x => x.Name).MaximumLength(150);
        FareAdjustmentValidationRules.Apply(
            RuleFor(x => x.SurchargePercent),
            RuleFor(x => x.RoundingStep));
    }
}

public sealed class UpsertFareCalendarDayCommandHandler
    : IRequestHandler<UpsertFareCalendarDayCommand, FareAdjustmentDto>
{
    private readonly IApplicationDbContext _context;

    public UpsertFareCalendarDayCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<FareAdjustmentDto> Handle(
        UpsertFareCalendarDayCommand request,
        CancellationToken cancellationToken)
    {
        var scope = FareAdjustmentScopes.Normalize(request.Scope);
        var adjustment = await _context.Set<FareAdjustment>()
            .SingleOrDefaultAsync(x => x.Scope == scope && x.Date == request.Date, cancellationToken);

        if (adjustment is null)
        {
            adjustment = new FareAdjustment
            {
                Scope = scope,
                Date = request.Date
            };
            _context.Set<FareAdjustment>().Add(adjustment);
        }

        adjustment.Name = string.IsNullOrWhiteSpace(request.Name)
            ? $"{scope} {request.Date:yyyy-MM-dd}"
            : request.Name.Trim();
        adjustment.SurchargePercent = request.SurchargePercent;
        adjustment.RoundingStep = request.RoundingStep;
        adjustment.IsActive = request.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
        return FareAdjustmentSupport.ToDto(adjustment);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record DeleteFareCalendarDayCommand(DateOnly Date, string Scope) : IRequest<Unit>;

public sealed class DeleteFareCalendarDayCommandHandler
    : IRequestHandler<DeleteFareCalendarDayCommand, Unit>
{
    private readonly IApplicationDbContext _context;

    public DeleteFareCalendarDayCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Unit> Handle(DeleteFareCalendarDayCommand request, CancellationToken cancellationToken)
    {
        var scope = FareAdjustmentScopes.Normalize(request.Scope);
        if (scope is not (FareAdjustmentScopes.Holiday or FareAdjustmentScopes.Special))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Scope),
                "scope chỉ nhận Holiday hoặc Special.")]);
        }

        var adjustment = await _context.Set<FareAdjustment>()
            .SingleOrDefaultAsync(x => x.Scope == scope && x.Date == request.Date, cancellationToken)
            ?? throw new NotFoundException("Fare adjustment not found.");

        _context.Set<FareAdjustment>().Remove(adjustment);
        await _context.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

file static class FareAdjustmentValidationRules
{
    public static void Apply<T>(
        IRuleBuilderInitial<T, decimal> surchargePercent,
        IRuleBuilderInitial<T, decimal> roundingStep)
    {
        surchargePercent
            .InclusiveBetween(0, 1000)
            .WithMessage("Phần trăm phụ thu phải từ 0 đến 1000.")
            .Must(x => decimal.Round(x, 2) == x)
            .WithMessage("Phần trăm phụ thu tối đa 2 chữ số thập phân.");
        roundingStep
            .Must(x => x is 1m or 100m or 500m or 1000m)
            .WithMessage("Bước làm tròn chỉ nhận 1, 100, 500 hoặc 1000 VND.");
    }
}
