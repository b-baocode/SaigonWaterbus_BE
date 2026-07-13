using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Routes;

[Authorize(Roles = "Admin")]
public sealed record UpdateRouteCommand(
    Guid RouteId,
    string RouteName,
    string? RouteType,
    string? Description,
    decimal? BaseDistanceKm,
    int? EstimatedDurationMin,
    string Status,
    bool? IsBookable = null) : IRequest<RouteDto>;

public sealed class UpdateRouteCommandValidator : AbstractValidator<UpdateRouteCommand>
{
    public UpdateRouteCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.RouteName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RouteType)
            .Must(x => string.IsNullOrWhiteSpace(x) || RouteTypes.IsValid(x))
            .WithMessage("RouteType phai la Regular, SightseeingLoop, hoac CharterReference.");
        RuleFor(x => x.Status).NotEmpty();
    }
}

public sealed class UpdateRouteCommandHandler : IRequestHandler<UpdateRouteCommand, RouteDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateRouteCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<RouteDto> Handle(UpdateRouteCommand request, CancellationToken cancellationToken)
    {
        var route = await _context.Set<Route>()
            .SingleOrDefaultAsync(r => r.Id == request.RouteId, cancellationToken)
            ?? throw new NotFoundException("Route not found.");

        route.RouteName = request.RouteName.Trim();
        route.RouteType = RouteTypes.Normalize(request.RouteType ?? route.RouteType);
        route.Description = request.Description?.Trim();
        route.BaseDistanceKm = request.BaseDistanceKm;
        route.EstimatedDurationMin = request.EstimatedDurationMin;
        route.Status = request.Status;
        if (request.IsBookable.HasValue)
        {
            route.IsBookable = request.IsBookable.Value;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new RouteDto(route.Id, route.RouteCode, route.RouteName,
            route.RouteType, route.Description, route.BaseDistanceKm, route.EstimatedDurationMin, route.Status, route.IsBookable);
    }
}
