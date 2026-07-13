using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.StaffWorkAssignments;

public sealed record StaffWorkAssignmentDto(
    Guid AssignmentId,
    Guid StaffUserId,
    string StaffName,
    StaffType? StaffType,
    StaffWorkAssignmentType AssignmentType,
    DateOnly WorkingDate,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    StaffWorkAssignmentStatus Status,
    string ShiftState,
    string? DutyRole,
    string? Note,
    StaffWorkAssignmentBoatDto? Boat,
    StaffWorkAssignmentStationDto? Station,
    Guid AssignedByUserId,
    string AssignedByName,
    DateTimeOffset AssignedAt);

public sealed record StaffWorkAssignmentBoatDto(
    Guid BoatId,
    string BoatCode,
    string BoatName);

public sealed record StaffWorkAssignmentStationDto(
    Guid StationId,
    string StationCode,
    string StationName);

public sealed record StaffCurrentShiftDto(
    StaffWorkAssignmentDto? CurrentShift,
    IReadOnlyList<StaffWorkAssignmentDto> TodayAssignments);

[Authorize(Roles = "Admin,Manager")]
public sealed record CreateStaffWorkAssignmentCommand(
    Guid StaffUserId,
    StaffWorkAssignmentType AssignmentType,
    Guid? BoatId = null,
    Guid? StationId = null,
    DateTimeOffset? StartAt = null,
    DateTimeOffset? EndAt = null,
    string? DutyRole = null,
    string? Note = null) : IRequest<StaffWorkAssignmentDto>;

public sealed class CreateStaffWorkAssignmentCommandValidator : AbstractValidator<CreateStaffWorkAssignmentCommand>
{
    public CreateStaffWorkAssignmentCommandValidator()
    {
        RuleFor(x => x.StaffUserId).NotEmpty();
        RuleFor(x => x.AssignmentType).IsInEnum();
        RuleFor(x => x.DutyRole).MaximumLength(80).When(x => x.DutyRole is not null);
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
        RuleFor(x => x.StartAt).NotNull().WithMessage("startAt là bắt buộc.");
        RuleFor(x => x.EndAt).NotNull().WithMessage("endAt là bắt buộc.");
        RuleFor(x => x)
            .Must(x => !x.StartAt.HasValue || !x.EndAt.HasValue || x.EndAt.Value > x.StartAt.Value)
            .WithMessage("endAt phải lớn hơn startAt.")
            .OverridePropertyName(nameof(CreateStaffWorkAssignmentCommand.EndAt));
    }
}

public sealed class CreateStaffWorkAssignmentCommandHandler
    : IRequestHandler<CreateStaffWorkAssignmentCommand, StaffWorkAssignmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CreateStaffWorkAssignmentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<StaffWorkAssignmentDto> Handle(
        CreateStaffWorkAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var staff = await StaffWorkAssignmentSupport.LoadAssignableStaffAsync(
            _context, request.StaffUserId, nameof(request.StaffUserId), cancellationToken);

        var resolved = await StaffWorkAssignmentSupport.ResolveTargetAsync(
            _context,
            request.AssignmentType,
            request.BoatId,
            request.StationId,
            request.StartAt,
            request.EndAt,
            cancellationToken);

        await StaffWorkAssignmentSupport.EnsureActorCanAssignAsync(
            _context,
            actor,
            staff,
            resolved,
            cancellationToken);

        await StaffWorkAssignmentSupport.EnsureStaffHasNoTimeConflictAsync(
            _context,
            staff.Id,
            resolved.StartAt,
            resolved.EndAt,
            null,
            cancellationToken);

        var assignment = new StaffWorkAssignment
        {
            StaffUserId = staff.Id,
            AssignmentType = resolved.AssignmentType,
            BoatId = resolved.Boat?.Id,
            StationId = resolved.Station?.Id,
            WorkingDate = StaffWorkAssignmentSupport.ResolveWorkingDate(resolved.StartAt),
            StartAt = resolved.StartAt,
            EndAt = resolved.EndAt,
            DutyRole = string.IsNullOrWhiteSpace(request.DutyRole) ? null : request.DutyRole.Trim(),
            Status = StaffWorkAssignmentStatus.Scheduled,
            AssignedByUserId = actor.Id,
            AssignedAt = _timeProvider.GetUtcNow(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };

        _context.StaffWorkAssignments.Add(assignment);
        await _context.SaveChangesAsync(cancellationToken);

        return await StaffWorkAssignmentSupport.LoadDtoAsync(
            _context,
            assignment.Id,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record GetStaffWorkAssignmentsQuery(
    DateOnly? FromDate,
    DateOnly? ToDate,
    Guid? StaffUserId = null,
    StaffWorkAssignmentType? AssignmentType = null,
    Guid? BoatId = null,
    Guid? StationId = null,
    StaffWorkAssignmentStatus? Status = null) : IRequest<IReadOnlyList<StaffWorkAssignmentDto>>;

public sealed class GetStaffWorkAssignmentsQueryValidator : AbstractValidator<GetStaffWorkAssignmentsQuery>
{
    public GetStaffWorkAssignmentsQueryValidator()
    {
        RuleFor(x => x)
            .Must(x => !x.FromDate.HasValue || !x.ToDate.HasValue || x.ToDate.Value >= x.FromDate.Value)
            .WithMessage("toDate phải lớn hơn hoặc bằng fromDate.")
            .OverridePropertyName(nameof(GetStaffWorkAssignmentsQuery.ToDate));
        RuleFor(x => x)
            .Must(x => !x.FromDate.HasValue || !x.ToDate.HasValue || x.FromDate.Value.AddDays(62) >= x.ToDate.Value)
            .WithMessage("Khoảng xem không được vượt quá 62 ngày.")
            .OverridePropertyName(nameof(GetStaffWorkAssignmentsQuery.ToDate));
    }
}

public sealed class GetStaffWorkAssignmentsQueryHandler
    : IRequestHandler<GetStaffWorkAssignmentsQuery, IReadOnlyList<StaffWorkAssignmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public GetStaffWorkAssignmentsQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<StaffWorkAssignmentDto>> Handle(
        GetStaffWorkAssignmentsQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var query = StaffWorkAssignmentSupport.BuildDtoQuery(_context);

        if (request.FromDate.HasValue)
            query = query.Where(x => x.WorkingDate >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(x => x.WorkingDate <= request.ToDate.Value);
        if (request.StaffUserId.HasValue)
            query = query.Where(x => x.StaffUserId == request.StaffUserId.Value);
        if (request.AssignmentType.HasValue)
            query = query.Where(x => x.AssignmentType == request.AssignmentType.Value);
        if (request.BoatId.HasValue)
            query = query.Where(x => x.BoatId == request.BoatId.Value);
        if (request.StationId.HasValue)
            query = query.Where(x => x.StationId == request.StationId.Value);
        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);

        if (AuthSupport.IsManager(actor))
        {
            var stationIds = await StaffWorkAssignmentSupport.GetManagedStationIdsAsync(
                _context, actor.Id, cancellationToken);
            query = query.Where(x =>
                x.AssignmentType == StaffWorkAssignmentType.Station
                && x.StationId.HasValue
                && stationIds.Contains(x.StationId.Value));
        }
        else if (!AuthSupport.IsAdmin(actor))
        {
            throw new ForbiddenAccessException();
        }

        var now = _timeProvider.GetUtcNow();
        var assignments = await query
            .OrderBy(x => x.StartAt)
            .ThenBy(x => x.StaffUser.FullName)
            .ToListAsync(cancellationToken);

        return assignments.Select(x => StaffWorkAssignmentSupport.ToDto(x, now)).ToList();
    }
}

[Authorize(Roles = "Staff")]
public sealed record GetMyStaffWorkAssignmentsQuery(DateOnly FromDate, DateOnly ToDate)
    : IRequest<IReadOnlyList<StaffWorkAssignmentDto>>;

public sealed class GetMyStaffWorkAssignmentsQueryValidator : AbstractValidator<GetMyStaffWorkAssignmentsQuery>
{
    public GetMyStaffWorkAssignmentsQueryValidator()
    {
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("toDate phải lớn hơn hoặc bằng fromDate.");
        RuleFor(x => x)
            .Must(x => x.FromDate.AddDays(62) >= x.ToDate)
            .WithMessage("Khoảng xem không được vượt quá 62 ngày.")
            .OverridePropertyName(nameof(GetMyStaffWorkAssignmentsQuery.ToDate));
    }
}

public sealed class GetMyStaffWorkAssignmentsQueryHandler
    : IRequestHandler<GetMyStaffWorkAssignmentsQuery, IReadOnlyList<StaffWorkAssignmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public GetMyStaffWorkAssignmentsQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<StaffWorkAssignmentDto>> Handle(
        GetMyStaffWorkAssignmentsQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        var now = _timeProvider.GetUtcNow();
        var assignments = await StaffWorkAssignmentSupport.BuildDtoQuery(_context)
            .Where(x => x.StaffUserId == actor.Id
                && x.WorkingDate >= request.FromDate
                && x.WorkingDate <= request.ToDate
                && x.Status != StaffWorkAssignmentStatus.Cancelled)
            .OrderBy(x => x.StartAt)
            .ToListAsync(cancellationToken);

        return assignments.Select(x => StaffWorkAssignmentSupport.ToDto(x, now)).ToList();
    }
}

[Authorize(Roles = "Staff")]
public sealed record GetMyCurrentStaffShiftQuery : IRequest<StaffCurrentShiftDto>;

public sealed class GetMyCurrentStaffShiftQueryHandler
    : IRequestHandler<GetMyCurrentStaffShiftQuery, StaffCurrentShiftDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public GetMyCurrentStaffShiftQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<StaffCurrentShiftDto> Handle(
        GetMyCurrentStaffShiftQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        var now = _timeProvider.GetUtcNow();
        var today = StaffWorkAssignmentSupport.ResolveWorkingDate(now);
        var todayAssignments = await StaffWorkAssignmentSupport.BuildDtoQuery(_context)
            .Where(x => x.StaffUserId == actor.Id
                && x.WorkingDate == today
                && x.Status != StaffWorkAssignmentStatus.Cancelled)
            .OrderBy(x => x.StartAt)
            .ToListAsync(cancellationToken);

        var todayDtos = todayAssignments
            .Select(x => StaffWorkAssignmentSupport.ToDto(x, now))
            .ToList();

        var current = todayDtos
            .Where(x => x.StartAt <= now && x.EndAt >= now)
            .OrderBy(x => x.StartAt)
            .FirstOrDefault();

        return new StaffCurrentShiftDto(current, todayDtos);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record UpdateStaffWorkAssignmentStatusCommand(
    Guid AssignmentId,
    StaffWorkAssignmentStatus Status) : IRequest<StaffWorkAssignmentDto>;

public sealed class UpdateStaffWorkAssignmentStatusCommandValidator
    : AbstractValidator<UpdateStaffWorkAssignmentStatusCommand>
{
    public UpdateStaffWorkAssignmentStatusCommandValidator()
    {
        RuleFor(x => x.AssignmentId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class UpdateStaffWorkAssignmentStatusCommandHandler
    : IRequestHandler<UpdateStaffWorkAssignmentStatusCommand, StaffWorkAssignmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateStaffWorkAssignmentStatusCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<StaffWorkAssignmentDto> Handle(
        UpdateStaffWorkAssignmentStatusCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var assignment = await _context.StaffWorkAssignments
            .SingleOrDefaultAsync(x => x.Id == request.AssignmentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phân công ca làm.");

        await StaffWorkAssignmentSupport.EnsureActorCanManageExistingAssignmentAsync(
            _context,
            actor,
            assignment,
            cancellationToken);

        assignment.Status = request.Status;
        await _context.SaveChangesAsync(cancellationToken);

        return await StaffWorkAssignmentSupport.LoadDtoAsync(
            _context,
            assignment.Id,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record DeleteStaffWorkAssignmentCommand(Guid AssignmentId) : IRequest;

public sealed class DeleteStaffWorkAssignmentCommandValidator
    : AbstractValidator<DeleteStaffWorkAssignmentCommand>
{
    public DeleteStaffWorkAssignmentCommandValidator()
    {
        RuleFor(x => x.AssignmentId).NotEmpty();
    }
}

public sealed class DeleteStaffWorkAssignmentCommandHandler
    : IRequestHandler<DeleteStaffWorkAssignmentCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DeleteStaffWorkAssignmentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task Handle(DeleteStaffWorkAssignmentCommand request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var assignment = await _context.StaffWorkAssignments
            .SingleOrDefaultAsync(x => x.Id == request.AssignmentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phân công ca làm.");

        await StaffWorkAssignmentSupport.EnsureActorCanManageExistingAssignmentAsync(
            _context,
            actor,
            assignment,
            cancellationToken);

        assignment.Status = StaffWorkAssignmentStatus.Cancelled;
        await _context.SaveChangesAsync(cancellationToken);
    }
}

public static class StaffWorkAssignmentSupport
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public sealed record ResolvedAssignmentTarget(
        StaffWorkAssignmentType AssignmentType,
        Boat? Boat,
        Station? Station,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt);

    public static async Task<User> LoadAssignableStaffAsync(
        IApplicationDbContext context,
        Guid staffUserId,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var staff = await context.Users
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == staffUserId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy staff.");

        if (!AuthSupport.IsStaff(staff))
        {
            throw new ValidationException([new ValidationFailure(propertyName, "Người được phân công phải có role Staff.")]);
        }

        if (staff.Status != UserStatus.Active)
        {
            throw new ValidationException([new ValidationFailure(propertyName, "Staff phải đang Active để được phân công.")]);
        }

        return staff;
    }

    public static async Task<ResolvedAssignmentTarget> ResolveTargetAsync(
        IApplicationDbContext context,
        StaffWorkAssignmentType assignmentType,
        Guid? boatId,
        Guid? stationId,
        DateTimeOffset? startAt,
        DateTimeOffset? endAt,
        CancellationToken cancellationToken)
    {
        return assignmentType switch
        {
            StaffWorkAssignmentType.Boat => await ResolveBoatTargetAsync(
                context, boatId, stationId, startAt, endAt, cancellationToken),
            StaffWorkAssignmentType.Station => await ResolveStationTargetAsync(
                context, boatId, stationId, startAt, endAt, cancellationToken),
            _ => throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.AssignmentType),
                "assignmentType không hợp lệ.")])
        };
    }

    public static async Task EnsureActorCanAssignAsync(
        IApplicationDbContext context,
        User actor,
        User staff,
        ResolvedAssignmentTarget target,
        CancellationToken cancellationToken)
    {
        if (target.AssignmentType == StaffWorkAssignmentType.Boat)
        {
            if (!AuthSupport.IsAdmin(actor))
            {
                throw new ForbiddenAccessException();
            }

            if (staff.StaffType != StaffType.OnBoard)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(CreateStaffWorkAssignmentCommand.StaffUserId),
                    "Staff lên tàu phải có staffType = OnBoard.")]);
            }

            return;
        }

        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor))
        {
            throw new ForbiddenAccessException();
        }

        if (staff.StaffType != StaffType.Ground)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.StaffUserId),
                "Staff ở bến phải có staffType = Ground.")]);
        }

        var stationId = target.Station?.Id
            ?? throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.StationId),
                "stationId là bắt buộc khi phân công theo Station.")]);

        if (AuthSupport.IsManager(actor))
        {
            var managerStationIds = await GetManagedStationIdsAsync(context, actor.Id, cancellationToken);
            if (!managerStationIds.Contains(stationId))
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(CreateStaffWorkAssignmentCommand.StationId),
                    "Manager chỉ được phân ca trong các bến mình phụ trách.")]);
            }
        }

        var staffBelongsToStation = await context.Set<UserStationAssignment>()
            .AnyAsync(x => x.UserId == staff.Id && x.StationId == stationId && x.IsActive, cancellationToken);
        if (!staffBelongsToStation)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.StaffUserId),
                "Staff chưa được gắn vào bến này.")]);
        }
    }

    public static async Task EnsureActorCanManageExistingAssignmentAsync(
        IApplicationDbContext context,
        User actor,
        StaffWorkAssignment assignment,
        CancellationToken cancellationToken)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        if (AuthSupport.IsManager(actor)
            && assignment.AssignmentType == StaffWorkAssignmentType.Station
            && assignment.StationId.HasValue)
        {
            var managerStationIds = await GetManagedStationIdsAsync(context, actor.Id, cancellationToken);
            if (managerStationIds.Contains(assignment.StationId.Value))
            {
                return;
            }
        }

        throw new ForbiddenAccessException();
    }

    public static async Task<IReadOnlyList<Guid>> GetManagedStationIdsAsync(
        IApplicationDbContext context,
        Guid managerUserId,
        CancellationToken cancellationToken) =>
        await context.Set<UserStationAssignment>()
            .Where(x => x.UserId == managerUserId && x.IsActive)
            .Select(x => x.StationId)
            .ToListAsync(cancellationToken);

    public static async Task EnsureStaffHasNoTimeConflictAsync(
        IApplicationDbContext context,
        Guid staffUserId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Guid? excludingAssignmentId,
        CancellationToken cancellationToken)
    {
        var hasConflict = await context.StaffWorkAssignments.AnyAsync(
            x => x.StaffUserId == staffUserId
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && (!excludingAssignmentId.HasValue || x.Id != excludingAssignmentId.Value)
                && x.StartAt < endAt
                && startAt < x.EndAt,
            cancellationToken);
        if (hasConflict)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.StaffUserId),
                "Staff này đã có ca làm trùng thời gian.")]);
        }
    }

    public static IQueryable<StaffWorkAssignment> BuildDtoQuery(IApplicationDbContext context) =>
        context.StaffWorkAssignments
            .AsNoTracking()
            .Include(x => x.StaffUser)
            .Include(x => x.AssignedByUser)
            .Include(x => x.Boat)
            .Include(x => x.Station);

    public static async Task<StaffWorkAssignmentDto> LoadDtoAsync(
        IApplicationDbContext context,
        Guid assignmentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var assignment = await BuildDtoQuery(context)
            .SingleOrDefaultAsync(x => x.Id == assignmentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phân công ca làm.");

        return ToDto(assignment, now);
    }

    public static StaffWorkAssignmentDto ToDto(StaffWorkAssignment assignment, DateTimeOffset now) =>
        new(
            assignment.Id,
            assignment.StaffUserId,
            assignment.StaffUser.FullName,
            assignment.StaffUser.StaffType,
            assignment.AssignmentType,
            assignment.WorkingDate,
            assignment.StartAt,
            assignment.EndAt,
            assignment.Status,
            ResolveShiftState(assignment, now),
            assignment.DutyRole,
            assignment.Note,
            assignment.Boat is null
                ? null
                : new StaffWorkAssignmentBoatDto(
                    assignment.Boat.Id,
                    assignment.Boat.Code,
                    assignment.Boat.Name),
            assignment.Station is null
                ? null
                : new StaffWorkAssignmentStationDto(
                    assignment.Station.Id,
                    assignment.Station.StationCode,
                    assignment.Station.StationName),
            assignment.AssignedByUserId,
            assignment.AssignedByUser.FullName,
            assignment.AssignedAt);

    public static DateOnly ResolveWorkingDate(DateTimeOffset startAt) =>
        DateOnly.FromDateTime(startAt.ToOffset(VietnamOffset).DateTime);

    public static string ResolveShiftState(StaffWorkAssignment assignment, DateTimeOffset now)
    {
        if (assignment.Status == StaffWorkAssignmentStatus.Cancelled)
        {
            return "Cancelled";
        }

        if (assignment.Status == StaffWorkAssignmentStatus.Completed || now > assignment.EndAt)
        {
            return "Completed";
        }

        if (assignment.Status == StaffWorkAssignmentStatus.Active
            || (assignment.StartAt <= now && assignment.EndAt >= now))
        {
            return "Active";
        }

        return "Upcoming";
    }

    private static async Task<ResolvedAssignmentTarget> ResolveBoatTargetAsync(
        IApplicationDbContext context,
        Guid? boatId,
        Guid? stationId,
        DateTimeOffset? startAt,
        DateTimeOffset? endAt,
        CancellationToken cancellationToken)
    {
        if (stationId.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.StationId),
                "Không gửi stationId khi assignmentType = Boat.")]);
        }

        if (!boatId.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.BoatId),
                "boatId là bắt buộc khi assignmentType = Boat.")]);
        }

        var boat = await context.Boats
            .SingleOrDefaultAsync(x => x.Id == boatId.Value, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tàu.");

        if (boat.Status != BoatStatus.Active)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.BoatId),
                "Chỉ phân staff vào tàu đang Active.")]);
        }

        if (!startAt.HasValue || !endAt.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.StartAt),
                "startAt và endAt là bắt buộc khi assignmentType = Boat.")]);
        }

        EnsureValidTimeRange(startAt.Value, endAt.Value);
        return new ResolvedAssignmentTarget(
            StaffWorkAssignmentType.Boat,
            boat,
            null,
            startAt.Value.ToUniversalTime(),
            endAt.Value.ToUniversalTime());
    }

    private static async Task<ResolvedAssignmentTarget> ResolveStationTargetAsync(
        IApplicationDbContext context,
        Guid? boatId,
        Guid? stationId,
        DateTimeOffset? startAt,
        DateTimeOffset? endAt,
        CancellationToken cancellationToken)
    {
        if (boatId.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.BoatId),
                "Không gửi boatId khi assignmentType = Station.")]);
        }

        if (!stationId.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.StationId),
                "stationId là bắt buộc khi assignmentType = Station.")]);
        }

        var station = await context.Set<Station>()
            .SingleOrDefaultAsync(x => x.Id == stationId.Value, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy bến.");

        if (station.Status != StationStatus.Active)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.StationId),
                "Chỉ phân staff vào bến đang Active.")]);
        }

        if (!startAt.HasValue || !endAt.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.StartAt),
                "startAt và endAt là bắt buộc khi assignmentType = Station.")]);
        }

        EnsureValidTimeRange(startAt.Value, endAt.Value);
        return new ResolvedAssignmentTarget(
            StaffWorkAssignmentType.Station,
            null,
            station,
            startAt.Value.ToUniversalTime(),
            endAt.Value.ToUniversalTime());
    }

    private static void EnsureValidTimeRange(DateTimeOffset startAt, DateTimeOffset endAt)
    {
        if (endAt <= startAt)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.EndAt),
                "endAt phải lớn hơn startAt.")]);
        }
    }
}
