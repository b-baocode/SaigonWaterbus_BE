using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.BoatStaffAssignments;

public sealed record GetBoatStaffAssignmentsQuery(
    Guid BoatId,
    DateOnly? WorkingDate,
    string? ShiftCode,
    bool ActiveOnly = true) : IRequest<IReadOnlyList<BoatStaffAssignmentDto>>;

public sealed class GetBoatStaffAssignmentsQueryHandler
    : IRequestHandler<GetBoatStaffAssignmentsQuery, IReadOnlyList<BoatStaffAssignmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBoatStaffAssignmentsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BoatStaffAssignmentDto>> Handle(
        GetBoatStaffAssignmentsQuery request,
        CancellationToken cancellationToken)
    {
        await BoatStaffAssignmentSupport.EnsureCurrentUserCanViewBoatStaffAsync(
            _context,
            _userContext,
            cancellationToken);

        var query = _context.BoatStaffAssignments
            .Include(x => x.Boat)
            .Include(x => x.StaffUser)
            .Include(x => x.AssignedByUser)
            .Include(x => x.ReplacedByUser)
            .AsNoTracking()
            .Where(x => x.BoatId == request.BoatId);

        if (request.WorkingDate.HasValue)
        {
            query = query.Where(x => x.WorkingDate == request.WorkingDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.ShiftCode))
        {
            var shiftCode = BoatStaffAssignmentSupport.NormalizeShiftCode(request.ShiftCode);
            query = query.Where(x => x.ShiftCode == shiftCode);
        }

        if (request.ActiveOnly)
        {
            query = query.Where(x => x.IsActive);
        }

        var assignments = await query
            .OrderBy(x => x.WorkingDate)
            .ThenBy(x => x.ShiftCode)
            .ThenBy(x => x.DutyRole)
            .ThenBy(x => x.StaffUser.FullName)
            .ToListAsync(cancellationToken);

        return assignments.Select(BoatStaffAssignmentSupport.ToDto).ToList();
    }
}
