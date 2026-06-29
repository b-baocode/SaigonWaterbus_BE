using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.BoatStaffAssignments;

public sealed record AssignBoatStaffCommand(
    Guid BoatId,
    Guid StaffUserId,
    DateOnly WorkingDate,
    string? ShiftCode,
    string? DutyRole) : IRequest<BoatStaffAssignmentDto>;

public sealed class AssignBoatStaffCommandValidator : AbstractValidator<AssignBoatStaffCommand>
{
    public AssignBoatStaffCommandValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x.StaffUserId).NotEmpty();
        RuleFor(x => x.WorkingDate).NotEmpty();
        RuleFor(x => x.ShiftCode)
            .MaximumLength(30)
            .Must(BoatStaffAssignmentSupport.IsValidShiftCode)
            .WithMessage("Ca làm việc chỉ được là Day hoặc Evening.");
        RuleFor(x => x.DutyRole).MaximumLength(50);
    }
}

public sealed class AssignBoatStaffCommandHandler : IRequestHandler<AssignBoatStaffCommand, BoatStaffAssignmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public AssignBoatStaffCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<BoatStaffAssignmentDto> Handle(
        AssignBoatStaffCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await BoatStaffAssignmentSupport.EnsureCurrentUserCanManageBoatStaffAsync(
            _context,
            _userContext,
            cancellationToken);

        var boat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tàu.");

        if (boat.Status is BoatStatus.Retired)
        {
            throw SaigonWaterbus.Application.Auth.Common.AuthSupport.CreateValidationException(
                nameof(request.BoatId),
                "Không thể phân công staff cho tàu đã Retired.");
        }

        var shiftCode = BoatStaffAssignmentSupport.NormalizeShiftCode(request.ShiftCode);
        await BoatStaffAssignmentSupport.EnsureStaffCanBeAssignedAsync(
            _context,
            request.StaffUserId,
            nameof(request.StaffUserId),
            cancellationToken);
        await BoatStaffAssignmentSupport.EnsureStaffIsAvailableAsync(
            _context,
            request.StaffUserId,
            request.WorkingDate,
            shiftCode,
            null,
            cancellationToken);

        var assignment = new BoatStaffAssignment
        {
            BoatId = boat.Id,
            Boat = boat,
            StaffUserId = request.StaffUserId,
            WorkingDate = request.WorkingDate,
            ShiftCode = shiftCode,
            DutyRole = BoatStaffAssignmentSupport.NormalizeOptional(request.DutyRole),
            IsActive = true,
            AssignedByUserId = actor.Id,
            AssignedByUser = actor,
            AssignedAt = _timeProvider.GetUtcNow()
        };

        _context.BoatStaffAssignments.Add(assignment);
        await _context.SaveChangesAsync(cancellationToken);

        return await LoadDtoAsync(assignment.Id, cancellationToken);
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
