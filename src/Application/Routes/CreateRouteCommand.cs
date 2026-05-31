using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

public sealed record CreateRouteCommand(
    string RouteCode,
    string RouteName,
    string? Description,
    decimal? BaseDistanceKm,
    int? EstimatedDurationMin) : IRequest<RouteDto>;

public sealed class CreateRouteCommandValidator : AbstractValidator<CreateRouteCommand>
{
    public CreateRouteCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.RouteName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.BaseDistanceKm).GreaterThanOrEqualTo(0).When(x => x.BaseDistanceKm.HasValue);
        RuleFor(x => x.EstimatedDurationMin).GreaterThan(0).When(x => x.EstimatedDurationMin.HasValue);
    }
}

public sealed class CreateRouteCommandHandler : IRequestHandler<CreateRouteCommand, RouteDto>
{
    private readonly IApplicationDbContext _context;

    public CreateRouteCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<RouteDto> Handle(CreateRouteCommand request, CancellationToken cancellationToken)
    {
        var code = request.RouteCode.Trim().ToUpperInvariant();

        if (await _context.Set<Route>().AnyAsync(r => r.RouteCode == code, cancellationToken))
            throw new ValidationException([new ValidationFailure(nameof(request.RouteCode), "Route code already exists.")]);

        var route = new Route
        {
            RouteCode = code,
            RouteName = request.RouteName.Trim(),
            Description = request.Description?.Trim(),
            BaseDistanceKm = request.BaseDistanceKm,
            EstimatedDurationMin = request.EstimatedDurationMin,
            Status = "Active"
        };

        _context.Set<Route>().Add(route);
        await _context.SaveChangesAsync(cancellationToken);

        return new RouteDto(route.Id, route.RouteCode, route.RouteName,
            route.Description, route.BaseDistanceKm, route.EstimatedDurationMin, route.Status);
    }
}
