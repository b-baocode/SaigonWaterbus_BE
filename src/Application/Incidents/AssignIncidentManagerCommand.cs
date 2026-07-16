using SaigonWaterbus.Application.Common.Interfaces;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Incidents;

public sealed record AssignIncidentManagerCommand(
    Guid IncidentId,
    Guid ManagerUserId) : IRequest<IncidentDto>;

public sealed class AssignIncidentManagerCommandValidator : AbstractValidator<AssignIncidentManagerCommand>
{
    public AssignIncidentManagerCommandValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty();
        RuleFor(x => x.ManagerUserId).NotEmpty();
    }
}

public sealed class AssignIncidentManagerCommandHandler : IRequestHandler<AssignIncidentManagerCommand, IncidentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly IIncidentRealtimeNotifier _realtimeNotifier;

    public AssignIncidentManagerCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IIncidentRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullIncidentRealtimeNotifier.Instance;
    }

    public async Task<IncidentDto> Handle(
        AssignIncidentManagerCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await IncidentSupport.EnsureCurrentUserCanAssignIncidentManagerAsync(
            _context,
            _userContext,
            cancellationToken);

        await IncidentSupport.EnsureUserIsActiveManagerAsync(
            _context,
            request.ManagerUserId,
            nameof(request.ManagerUserId),
            cancellationToken);

        var incident = await LoadIncidentQuery()
            .SingleOrDefaultAsync(x => x.Id == request.IncidentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy sự cố.");

        incident.AssignedManagerId = request.ManagerUserId;
        incident.AssignedAt = _timeProvider.GetUtcNow();
        incident.AssignedByUserId = actor.Id;
        incident.AssignedByUser = actor;

        await _context.SaveChangesAsync(cancellationToken);

        incident = await LoadIncidentQuery().SingleAsync(x => x.Id == request.IncidentId, cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            IncidentSupport.ToRealtimeEvent(incident, "IncidentAssigned", incident.AssignedAt),
            cancellationToken);
        return IncidentSupport.ToDto(incident);
    }

    private IQueryable<Domain.Entities.Incident> LoadIncidentQuery() =>
        _context.Incidents
            .Include(x => x.Boat)
            .Include(x => x.Trip)
            .Include(x => x.Reporter)
            .Include(x => x.AssignedManager)
            .Include(x => x.AssignedByUser)
            .Include(x => x.ReplacementBoat)
            .Include(x => x.ReplacementAssignedByUser)
            .Include(x => x.Resolver);
}
