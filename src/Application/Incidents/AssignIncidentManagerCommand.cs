using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

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

    public AssignIncidentManagerCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<IncidentDto> Handle(AssignIncidentManagerCommand request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor))
        {
            throw new ForbiddenAccessException();
        }

        var manager = await _context.Users
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == request.ManagerUserId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy manager.");
        EnsureAssignableManager(manager);

        var incident = await _context.Incidents
            .Include(x => x.Boat)
            .Include(x => x.Trip)
            .Include(x => x.Reporter)
            .Include(x => x.AssignedManager)
            .Include(x => x.AssignedByUser)
            .Include(x => x.RescueBoat)
            .Include(x => x.RescueDispatchedByUser)
            .Include(x => x.ReplacementBoat)
            .Include(x => x.ReplacementAssignedByUser)
            .Include(x => x.ReplacementTargetStation)
            .Include(x => x.Resolver)
            .SingleOrDefaultAsync(x => x.Id == request.IncidentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy sự cố.");

        var assignedAt = _timeProvider.GetUtcNow();
        incident.AssignedManagerId = manager.Id;
        incident.AssignedManager = manager;
        incident.AssignedAt = assignedAt;
        incident.AssignedByUserId = actor.Id;
        incident.AssignedByUser = actor;

        await _context.SaveChangesAsync(cancellationToken);
        return IncidentSupport.ToDto(incident, incident.ActiveTicketCountSnapshot);
    }

    private static void EnsureAssignableManager(User manager)
    {
        if (!string.Equals(manager.Role.SystemName, Roles.ManagerSystemName, StringComparison.Ordinal))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(AssignIncidentManagerCommand.ManagerUserId),
                "Người phụ trách phải có role Manager.")]);
        }

        if (manager.Status != UserStatus.Active)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(AssignIncidentManagerCommand.ManagerUserId),
                "Manager phải đang Active để được phân công.")]);
        }
    }
}
