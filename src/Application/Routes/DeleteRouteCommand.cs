using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

public sealed record DeleteRouteCommand(Guid RouteId) : IRequest;

public sealed class DeleteRouteCommandValidator : AbstractValidator<DeleteRouteCommand>
{
    public DeleteRouteCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
    }
}

public sealed class DeleteRouteCommandHandler : IRequestHandler<DeleteRouteCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteRouteCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task Handle(DeleteRouteCommand request, CancellationToken cancellationToken)
    {
        var route = await _context.Set<Route>()
            .SingleOrDefaultAsync(r => r.Id == request.RouteId, cancellationToken)
            ?? throw new NotFoundException("Route not found.");

        var hasTrips = await _context.Set<Trip>()
            .AnyAsync(t => t.RouteId == request.RouteId, cancellationToken);

        if (hasTrips)
            throw new ValidationException([new ValidationFailure(nameof(request.RouteId),
                "Cannot delete a route that has trips. Deactivate it instead.")]);

        _context.Set<Route>().Remove(route);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
