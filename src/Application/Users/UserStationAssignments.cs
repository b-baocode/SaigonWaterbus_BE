using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Users;

public sealed record UserStationAssignmentDto(
    Guid StationId,
    string StationCode,
    string StationName,
    bool IsPrimary,
    bool IsActive,
    DateTimeOffset AssignedAt);

public sealed record GetUserStationAssignmentsRequest(Guid UserId);

public sealed class GetUserStationAssignmentsRequestValidator : AbstractValidator<GetUserStationAssignmentsRequest>
{
    public GetUserStationAssignmentsRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId không hợp lệ.");
    }
}

public sealed class GetUserStationAssignmentsRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetUserStationAssignmentsRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyCollection<UserStationAssignmentDto>> ExecuteAsync(
        GetUserStationAssignmentsRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);
        var user = await UserManagementSupport.GetVisibleUserByIdAsync(_context, actor, request.UserId, cancellationToken);
        UserManagementSupport.EnsureCanViewStationAssignments(actor, user);

        return await _context.Set<UserStationAssignment>()
            .AsNoTracking()
            .Include(x => x.Station)
            .Where(x => x.UserId == request.UserId && x.IsActive)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Station.StationName)
            .Select(x => new UserStationAssignmentDto(
                x.StationId,
                x.Station.StationCode,
                x.Station.StationName,
                x.IsPrimary,
                x.IsActive,
                x.AssignedAt))
            .ToArrayAsync(cancellationToken);
    }
}

public sealed record AssignUserStationsRequest(
    Guid UserId,
    IReadOnlyCollection<Guid> StationIds,
    Guid? PrimaryStationId = null);

public sealed class AssignUserStationsRequestValidator : AbstractValidator<AssignUserStationsRequest>
{
    public AssignUserStationsRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId không hợp lệ.");

        RuleFor(x => x.StationIds)
            .NotEmpty()
            .WithMessage("Cần chọn ít nhất một bến.");

        RuleFor(x => x.StationIds)
            .Must(ids => ids.All(id => id != Guid.Empty))
            .WithMessage("StationId không hợp lệ.")
            .When(x => x.StationIds is not null);

        RuleFor(x => x.StationIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Danh sách bến không được trùng.")
            .When(x => x.StationIds is not null);

        RuleFor(x => x)
            .Must(x => !x.PrimaryStationId.HasValue || x.StationIds.Contains(x.PrimaryStationId.Value))
            .WithMessage("PrimaryStationId phải nằm trong danh sách stationIds.")
            .OverridePropertyName(nameof(AssignUserStationsRequest.PrimaryStationId))
            .When(x => x.StationIds is not null);
    }
}

public sealed class AssignUserStationsRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public AssignUserStationsRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyCollection<UserStationAssignmentDto>> ExecuteAsync(
        AssignUserStationsRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        UserManagementSupport.EnsureCanManageStationAssignments(actor);

        var target = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == request.UserId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy user.");

        UserManagementSupport.EnsureCanAssignStationsToUser(actor, target);

        var stationIds = request.StationIds.Distinct().ToArray();
        var stations = await _context.Set<Station>()
            .Where(x => stationIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (stations.Count != stationIds.Length)
        {
            throw AuthSupport.CreateValidationException(nameof(request.StationIds), "Một hoặc nhiều bến không tồn tại.");
        }

        var inactiveStation = stations.FirstOrDefault(x => x.Status != StationStatus.Active);
        if (inactiveStation is not null)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.StationIds),
                $"Bến '{inactiveStation.StationCode}' không ở trạng thái Active.");
        }

        var primaryStationId = request.PrimaryStationId ?? stationIds[0];
        var now = _timeProvider.GetUtcNow();

        var assignments = await _context.Set<UserStationAssignment>()
            .Include(x => x.Station)
            .Where(x => x.UserId == target.Id)
            .ToListAsync(cancellationToken);

        foreach (var assignment in assignments)
        {
            assignment.IsActive = stationIds.Contains(assignment.StationId);
            assignment.IsPrimary = assignment.IsActive && assignment.StationId == primaryStationId;
            if (assignment.IsActive)
            {
                assignment.AssignedAt = now;
                assignment.AssignedByUserId = actor.Id;
            }
        }

        var existingStationIds = assignments.Select(x => x.StationId).ToHashSet();
        foreach (var stationId in stationIds.Where(id => !existingStationIds.Contains(id)))
        {
            _context.Set<UserStationAssignment>().Add(new UserStationAssignment
            {
                UserId = target.Id,
                StationId = stationId,
                IsPrimary = stationId == primaryStationId,
                IsActive = true,
                AssignedAt = now,
                AssignedByUserId = actor.Id
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return await _context.Set<UserStationAssignment>()
            .AsNoTracking()
            .Include(x => x.Station)
            .Where(x => x.UserId == target.Id && x.IsActive)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Station.StationName)
            .Select(x => new UserStationAssignmentDto(
                x.StationId,
                x.Station.StationCode,
                x.Station.StationName,
                x.IsPrimary,
                x.IsActive,
                x.AssignedAt))
            .ToArrayAsync(cancellationToken);
    }
}
