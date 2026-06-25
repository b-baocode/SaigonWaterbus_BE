using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

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
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);

        var target = await _context.Set<User>()
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy người dùng.");

        UserManagementSupport.EnsureCanViewStationAssignments(actor, target);

        return await _context.Set<UserStationAssignment>()
            .Where(a => a.UserId == request.UserId)
            .OrderByDescending(a => a.IsPrimary)
            .ThenBy(a => a.Station.StationName)
            .Select(a => new UserStationAssignmentDto(
                a.StationId,
                a.Station.StationCode,
                a.Station.StationName,
                a.IsPrimary,
                a.IsActive,
                a.AssignedAt))
            .ToListAsync(cancellationToken);
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

        var target = await _context.Set<User>()
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy người dùng.");

        UserManagementSupport.EnsureCanAssignStationsToUser(actor, target);

        var stationIds = request.StationIds.Distinct().ToList();
        var existingStationIds = await _context.Set<Station>()
            .Where(s => stationIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var missing = stationIds.Except(existingStationIds).ToList();
        if (missing.Count > 0)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.StationIds), "Có bến không tồn tại.")]);
        }

        var primaryStationId = request.PrimaryStationId ?? stationIds[0];
        var now = _timeProvider.GetUtcNow();

        var existing = await _context.Set<UserStationAssignment>()
            .Where(a => a.UserId == request.UserId)
            .ToListAsync(cancellationToken);

        var toRemove = existing.Where(a => !stationIds.Contains(a.StationId)).ToList();
        _context.Set<UserStationAssignment>().RemoveRange(toRemove);

        foreach (var stationId in stationIds)
        {
            var isPrimary = stationId == primaryStationId;
            var current = existing.FirstOrDefault(a => a.StationId == stationId);
            if (current is null)
            {
                _context.Set<UserStationAssignment>().Add(new UserStationAssignment
                {
                    UserId = request.UserId,
                    StationId = stationId,
                    IsPrimary = isPrimary,
                    IsActive = true,
                    AssignedAt = now,
                    AssignedByUserId = actor.Id
                });
            }
            else
            {
                current.IsPrimary = isPrimary;
                current.IsActive = true;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return await _context.Set<UserStationAssignment>()
            .Where(a => a.UserId == request.UserId)
            .OrderByDescending(a => a.IsPrimary)
            .ThenBy(a => a.Station.StationName)
            .Select(a => new UserStationAssignmentDto(
                a.StationId,
                a.Station.StationCode,
                a.Station.StationName,
                a.IsPrimary,
                a.IsActive,
                a.AssignedAt))
            .ToListAsync(cancellationToken);
    }
}
