using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.BoatStaffAssignments;

public sealed record ReplaceBoatStaffCommand(
    Guid BoatId,
    Guid AssignmentId,
    Guid ReplacementStaffUserId,
    string Reason) : IRequest<BoatStaffAssignmentDto>;

public sealed class ReplaceBoatStaffCommandValidator : AbstractValidator<ReplaceBoatStaffCommand>
{
    public ReplaceBoatStaffCommandValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x.AssignmentId).NotEmpty();
        RuleFor(x => x.ReplacementStaffUserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class ReplaceBoatStaffCommandHandler : IRequestHandler<ReplaceBoatStaffCommand, BoatStaffAssignmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ReplaceBoatStaffCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<BoatStaffAssignmentDto> Handle(
        ReplaceBoatStaffCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await BoatStaffAssignmentSupport.EnsureCurrentUserCanManageBoatStaffAsync(
            _context,
            _userContext,
            cancellationToken);

        var oldAssignment = await _context.BoatStaffAssignments
            .Include(x => x.Boat)
            .Include(x => x.StaffUser)
            .SingleOrDefaultAsync(
                x => x.Id == request.AssignmentId && x.BoatId == request.BoatId,
                cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phân công staff.");

        if (!oldAssignment.IsActive)
        {
            throw SaigonWaterbus.Application.Auth.Common.AuthSupport.CreateValidationException(
                nameof(request.AssignmentId),
                "Phân công này đã không còn active.");
        }

        await BoatStaffAssignmentSupport.EnsureStaffCanBeAssignedAsync(
            _context,
            request.ReplacementStaffUserId,
            nameof(request.ReplacementStaffUserId),
            cancellationToken);
        await BoatStaffAssignmentSupport.EnsureStaffIsAvailableAsync(
            _context,
            request.ReplacementStaffUserId,
            oldAssignment.WorkingDate,
            oldAssignment.ShiftCode ?? BoatStaffAssignmentSupport.DefaultShiftCode,
            oldAssignment.Id,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var newAssignment = new BoatStaffAssignment
        {
            BoatId = oldAssignment.BoatId,
            Boat = oldAssignment.Boat,
            StaffUserId = request.ReplacementStaffUserId,
            WorkingDate = oldAssignment.WorkingDate,
            ShiftCode = oldAssignment.ShiftCode,
            DutyRole = oldAssignment.DutyRole,
            IsActive = true,
            AssignedByUserId = actor.Id,
            AssignedByUser = actor,
            AssignedAt = now,
            ReplacesAssignmentId = oldAssignment.Id
        };

        oldAssignment.IsActive = false;
        oldAssignment.ReplacementReason = request.Reason.Trim();
        oldAssignment.ReplacedAt = now;
        oldAssignment.ReplacedByUserId = actor.Id;
        oldAssignment.ReplacedByUser = actor;

        _context.BoatStaffAssignments.Add(newAssignment);
        await _context.SaveChangesAsync(cancellationToken);

        oldAssignment.ReplacedByAssignmentId = newAssignment.Id;
        await _context.SaveChangesAsync(cancellationToken);

        return await LoadDtoAsync(newAssignment.Id, cancellationToken);
    }

    private async Task<BoatStaffAssignmentDto> LoadDtoAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await _context.BoatStaffAssignments
            .Include(x => x.Boat)
            .Include(x => x.StaffUser)
            .Include(x => x.AssignedByUser)
            .Include(x => x.ReplacedByUser)
            .SingleAsync(x => x.Id == assignmentId, cancellationToken);

        return BoatStaffAssignmentSupport.ToDto(assignment);
    }
}
