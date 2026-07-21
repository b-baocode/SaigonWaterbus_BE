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
    StaffWorkAssignmentTripStopDto? TripStop,
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

public sealed record StaffWorkAssignmentTripStopDto(
    Guid TripStopId,
    Guid TripId,
    string TripCode,
    int StopOrder,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture);

public sealed record StaffCurrentShiftDto(
    StaffWorkAssignmentDto? CurrentShift,
    IReadOnlyList<StaffWorkAssignmentDto> TodayAssignments);

public sealed record StaffWorkAssignmentReplacementDto(
    StaffWorkAssignmentDto OriginalAssignment,
    StaffWorkAssignmentDto ReplacementAssignment);

public sealed record StaffAssignedTripDto(
    Guid TripId,
    string TripCode,
    string TripType,
    string TripStatus,
    DateOnly OperatingDate,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    Guid RouteId,
    string RouteCode,
    string RouteName,
    string RouteType,
    Guid? BoatId,
    string? BoatCode,
    string? BoatName,
    Guid AssignmentId,
    StaffWorkAssignmentType AssignmentType,
    DateTimeOffset AssignmentStartAt,
    DateTimeOffset AssignmentEndAt,
    string AssignmentShiftState,
    Guid? StationId,
    string? StationCode,
    string? StationName,
    Guid? TripStopId = null,
    int? StopOrder = null,
    DateTimeOffset? StopScheduledArrival = null,
    DateTimeOffset? StopScheduledDeparture = null);

[Authorize(Roles = "Admin,Manager")]
public sealed record CreateStaffWorkAssignmentCommand(
    Guid StaffUserId,
    StaffWorkAssignmentType AssignmentType,
    Guid? BoatId = null,
    Guid? StationId = null,
    Guid? TripStopId = null,
    DateTimeOffset? StartAt = null,
    DateTimeOffset? EndAt = null,
    string? DutyRole = null,
    string? Note = null) : IRequest<StaffWorkAssignmentDto>;

[Authorize(Roles = "Admin,Manager")]
public sealed record CreateBulkStaffWorkAssignmentsCommand(
    Guid StaffUserId,
    StaffWorkAssignmentType AssignmentType,
    Guid? BoatId,
    Guid? StationId,
    DateOnly FromDate,
    DateOnly ToDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    IReadOnlyCollection<int>? DaysOfWeek,
    string? DutyRole = null,
    string? Note = null) : IRequest<IReadOnlyList<StaffWorkAssignmentDto>>;

[Authorize(Roles = "Admin,Manager")]
public sealed record ReplaceStaffWorkAssignmentCommand(
    Guid AssignmentId,
    Guid ReplacementStaffUserId,
    string? Reason = null,
    string? Note = null) : IRequest<StaffWorkAssignmentReplacementDto>;

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

public sealed class CreateBulkStaffWorkAssignmentsCommandValidator
    : AbstractValidator<CreateBulkStaffWorkAssignmentsCommand>
{
    public CreateBulkStaffWorkAssignmentsCommandValidator()
    {
        RuleFor(x => x.StaffUserId).NotEmpty();
        RuleFor(x => x.AssignmentType).IsInEnum();
        RuleFor(x => x.DutyRole).MaximumLength(80).When(x => x.DutyRole is not null);
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
        RuleFor(x => x.FromDate)
            .NotEmpty()
            .WithMessage("fromDate là bắt buộc.");
        RuleFor(x => x.ToDate)
            .NotEmpty()
            .WithMessage("toDate là bắt buộc.");
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("toDate phải lớn hơn hoặc bằng fromDate.");
        RuleFor(x => x)
            .Must(x => x.FromDate.AddDays(93) >= x.ToDate)
            .WithMessage("Khoảng tạo lịch lặp không được vượt quá 93 ngày.")
            .OverridePropertyName(nameof(CreateBulkStaffWorkAssignmentsCommand.ToDate));
        RuleForEach(x => x.DaysOfWeek)
            .InclusiveBetween(1, 7)
            .WithMessage("daysOfWeek chỉ nhận 1-7, trong đó 1 là Thứ 2 và 7 là Chủ nhật.");
    }
}

public sealed class ReplaceStaffWorkAssignmentCommandValidator
    : AbstractValidator<ReplaceStaffWorkAssignmentCommand>
{
    public ReplaceStaffWorkAssignmentCommandValidator()
    {
        RuleFor(x => x.AssignmentId).NotEmpty();
        RuleFor(x => x.ReplacementStaffUserId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
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
            request.TripStopId,
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
            TripStopId = resolved.TripStop?.Id,
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

public sealed class CreateBulkStaffWorkAssignmentsCommandHandler
    : IRequestHandler<CreateBulkStaffWorkAssignmentsCommand, IReadOnlyList<StaffWorkAssignmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CreateBulkStaffWorkAssignmentsCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<StaffWorkAssignmentDto>> Handle(
        CreateBulkStaffWorkAssignmentsCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var staff = await StaffWorkAssignmentSupport.LoadAssignableStaffAsync(
            _context, request.StaffUserId, nameof(request.StaffUserId), cancellationToken);

        var occurrences = StaffWorkAssignmentSupport.BuildRecurringShiftOccurrences(
            request.FromDate,
            request.ToDate,
            request.StartTime,
            request.EndTime,
            request.DaysOfWeek);
        if (occurrences.Count == 0)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.DaysOfWeek),
                "Không có ngày nào phù hợp với daysOfWeek trong khoảng đã chọn.")]);
        }

        var firstOccurrence = occurrences[0];
        var resolved = await StaffWorkAssignmentSupport.ResolveTargetAsync(
            _context,
            request.AssignmentType,
            request.BoatId,
            request.StationId,
            tripStopId: null,
            firstOccurrence.StartAt,
            firstOccurrence.EndAt,
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
            occurrences.Select(x => (x.StartAt, x.EndAt)).ToList(),
            cancellationToken);

        var assignedAt = _timeProvider.GetUtcNow();
        var assignments = occurrences
            .Select(occurrence => StaffWorkAssignmentSupport.CreateAssignment(
                staff.Id,
                resolved,
                occurrence.StartAt,
                occurrence.EndAt,
                actor.Id,
                assignedAt,
                request.DutyRole,
                request.Note))
            .ToArray();

        _context.StaffWorkAssignments.AddRange(assignments);
        await _context.SaveChangesAsync(cancellationToken);

        var assignmentIds = assignments.Select(x => x.Id).ToArray();
        var savedAssignments = await StaffWorkAssignmentSupport.BuildDtoQuery(_context)
            .Where(x => assignmentIds.Contains(x.Id))
            .OrderBy(x => x.StartAt)
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        return savedAssignments.Select(x => StaffWorkAssignmentSupport.ToDto(x, now)).ToList();
    }
}

public sealed class ReplaceStaffWorkAssignmentCommandHandler
    : IRequestHandler<ReplaceStaffWorkAssignmentCommand, StaffWorkAssignmentReplacementDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ReplaceStaffWorkAssignmentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<StaffWorkAssignmentReplacementDto> Handle(
        ReplaceStaffWorkAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var assignment = await _context.StaffWorkAssignments
            .Include(x => x.StaffUser)
            .Include(x => x.Boat)
            .Include(x => x.Station)
            .Include(x => x.TripStop)
            .SingleOrDefaultAsync(x => x.Id == request.AssignmentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phân công ca làm.");

        await StaffWorkAssignmentSupport.EnsureActorCanManageExistingAssignmentAsync(
            _context,
            actor,
            assignment,
            cancellationToken);

        if (assignment.Status is StaffWorkAssignmentStatus.Cancelled or StaffWorkAssignmentStatus.Replaced)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.AssignmentId),
                "Ca này đã bị hủy hoặc đã được thay thế.")]);
        }

        var now = _timeProvider.GetUtcNow();
        if (assignment.EndAt <= now)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.AssignmentId),
                "Không thể thay thế ca đã kết thúc.")]);
        }

        if (assignment.StaffUserId == request.ReplacementStaffUserId)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ReplacementStaffUserId),
                "Nhân viên thay thế không được trùng với nhân viên hiện tại.")]);
        }

        var replacementStaff = await StaffWorkAssignmentSupport.LoadAssignableStaffAsync(
            _context,
            request.ReplacementStaffUserId,
            nameof(request.ReplacementStaffUserId),
            cancellationToken);
        var resolved = new StaffWorkAssignmentSupport.ResolvedAssignmentTarget(
            assignment.AssignmentType,
            assignment.Boat,
            assignment.Station,
            assignment.TripStop,
            assignment.StartAt,
            assignment.EndAt);

        await StaffWorkAssignmentSupport.EnsureActorCanAssignAsync(
            _context,
            actor,
            replacementStaff,
            resolved,
            cancellationToken);

        await StaffWorkAssignmentSupport.EnsureStaffHasNoTimeConflictAsync(
            _context,
            replacementStaff.Id,
            assignment.StartAt,
            assignment.EndAt,
            null,
            cancellationToken);

        assignment.Status = StaffWorkAssignmentStatus.Replaced;
        assignment.Note = StaffWorkAssignmentSupport.AppendReplacementNote(
            assignment.Note,
            replacementStaff.FullName,
            request.Reason);

        var replacement = StaffWorkAssignmentSupport.CreateAssignment(
            replacementStaff.Id,
            resolved,
            assignment.StartAt,
            assignment.EndAt,
            actor.Id,
            now,
            assignment.DutyRole,
            string.IsNullOrWhiteSpace(request.Note)
                ? $"Thay thế ca của {assignment.StaffUser.FullName}."
                : request.Note);

        _context.StaffWorkAssignments.Add(replacement);
        await _context.SaveChangesAsync(cancellationToken);

        var originalDto = await StaffWorkAssignmentSupport.LoadDtoAsync(
            _context,
            assignment.Id,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        var replacementDto = await StaffWorkAssignmentSupport.LoadDtoAsync(
            _context,
            replacement.Id,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        return new StaffWorkAssignmentReplacementDto(originalDto, replacementDto);
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
    Guid? TripStopId = null,
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
        if (request.TripStopId.HasValue)
            query = query.Where(x => x.TripStopId == request.TripStopId.Value);
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
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced)
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
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced)
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

[Authorize(Roles = "Staff")]
public sealed record GetMyStaffTripsQuery(DateOnly Date) : IRequest<IReadOnlyList<StaffAssignedTripDto>>;

public sealed class GetMyStaffTripsQueryHandler
    : IRequestHandler<GetMyStaffTripsQuery, IReadOnlyList<StaffAssignedTripDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public GetMyStaffTripsQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<StaffAssignedTripDto>> Handle(
        GetMyStaffTripsQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        var assignments = await _context.StaffWorkAssignments
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Station)
            .Include(x => x.TripStop)
            .Where(x => x.StaffUserId == actor.Id
                && x.WorkingDate == request.Date
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced
                && ((x.AssignmentType == StaffWorkAssignmentType.Boat && x.BoatId.HasValue)
                    || (x.AssignmentType == StaffWorkAssignmentType.Station && x.StationId.HasValue)))
            .OrderBy(x => x.StartAt)
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
        {
            return [];
        }

        var windowStart = assignments.Min(x => x.StartAt);
        var windowEnd = assignments.Max(x => x.EndAt);
        var trips = await _context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
            .Where(x => x.TripStatus != TripStatus.Cancelled
                && x.DepartureTime < windowEnd
                && windowStart < x.ArrivalTime)
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        return trips
            .Select(trip => new { Trip = trip, Assignment = FindBestMatchingAssignment(trip, assignments) })
            .Where(x => x.Assignment is not null)
            .Select(x => ToAssignedTripDto(x.Trip, x.Assignment!, now))
            .OrderBy(x => x.DepartureTime)
            .ThenBy(x => x.TripCode)
            .ToList();
    }

    private static StaffWorkAssignment? FindBestMatchingAssignment(
        Trip trip,
        IReadOnlyList<StaffWorkAssignment> assignments) =>
        assignments
            .Where(assignment => MatchesTrip(assignment, trip))
            .OrderByDescending(assignment => OverlapTicks(assignment.StartAt, assignment.EndAt, trip.DepartureTime, trip.ArrivalTime))
            .ThenBy(assignment => assignment.AssignmentType == StaffWorkAssignmentType.Boat ? 0 : 1)
            .ThenBy(assignment => assignment.StartAt)
            .FirstOrDefault();

    private static bool MatchesTrip(StaffWorkAssignment assignment, Trip trip)
    {
        if (!TimeRangesOverlap(assignment.StartAt, assignment.EndAt, trip.DepartureTime, trip.ArrivalTime))
        {
            return false;
        }

        return assignment.AssignmentType switch
        {
            StaffWorkAssignmentType.Boat => trip.BoatId.HasValue
                && assignment.BoatId.HasValue
                && trip.BoatId.Value == assignment.BoatId.Value,
            StaffWorkAssignmentType.Station when assignment.TripStopId.HasValue =>
                trip.TripStops.Any(stop => stop.Id == assignment.TripStopId.Value),
            StaffWorkAssignmentType.Station => assignment.StationId.HasValue
                && trip.Route.RouteStops.Any(stop => stop.StationId == assignment.StationId.Value),
            _ => false
        };
    }

    private static StaffAssignedTripDto ToAssignedTripDto(
        Trip trip,
        StaffWorkAssignment assignment,
        DateTimeOffset now)
    {
        var station = assignment.AssignmentType == StaffWorkAssignmentType.Station
            ? assignment.Station
            : null;
        var tripStop = assignment.TripStop;

        return new StaffAssignedTripDto(
            trip.Id,
            trip.TripCode,
            trip.TripType,
            trip.TripStatus.ToString(),
            trip.OperatingDate,
            trip.DepartureTime,
            trip.ArrivalTime,
            trip.RouteId,
            trip.Route.RouteCode,
            trip.Route.RouteName,
            trip.Route.RouteType,
            trip.BoatId,
            trip.Boat?.Code,
            trip.Boat?.Name,
            assignment.Id,
            assignment.AssignmentType,
            assignment.StartAt,
            assignment.EndAt,
            StaffWorkAssignmentSupport.ResolveShiftState(assignment, now),
            station?.Id,
            station?.StationCode,
            station?.StationName,
            tripStop?.Id,
            tripStop?.StopOrder,
            tripStop?.PlannedArrivalTime,
            tripStop?.PlannedDepartureTime);
    }

    private static bool TimeRangesOverlap(
        DateTimeOffset firstStart,
        DateTimeOffset firstEnd,
        DateTimeOffset secondStart,
        DateTimeOffset secondEnd) =>
        firstStart < secondEnd && secondStart < firstEnd;

    private static long OverlapTicks(
        DateTimeOffset firstStart,
        DateTimeOffset firstEnd,
        DateTimeOffset secondStart,
        DateTimeOffset secondEnd)
    {
        var start = firstStart > secondStart ? firstStart : secondStart;
        var end = firstEnd < secondEnd ? firstEnd : secondEnd;
        return end > start ? (end - start).Ticks : 0;
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
    private static readonly TimeSpan MaxSingleAssignmentDuration = TimeSpan.FromHours(24);

    public sealed record ResolvedAssignmentTarget(
        StaffWorkAssignmentType AssignmentType,
        Boat? Boat,
        Station? Station,
        TripStop? TripStop,
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
        Guid? tripStopId,
        DateTimeOffset? startAt,
        DateTimeOffset? endAt,
        CancellationToken cancellationToken)
    {
        return assignmentType switch
        {
            StaffWorkAssignmentType.Boat => await ResolveBoatTargetAsync(
                context, boatId, stationId, tripStopId, startAt, endAt, cancellationToken),
            StaffWorkAssignmentType.Station => await ResolveStationTargetAsync(
                context, boatId, stationId, tripStopId, startAt, endAt, cancellationToken),
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
                && x.Status != StaffWorkAssignmentStatus.Replaced
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

    public static async Task EnsureStaffHasNoTimeConflictAsync(
        IApplicationDbContext context,
        Guid staffUserId,
        IReadOnlyCollection<(DateTimeOffset StartAt, DateTimeOffset EndAt)> occurrences,
        CancellationToken cancellationToken)
    {
        if (occurrences.Count == 0)
        {
            return;
        }

        var windowStart = occurrences.Min(x => x.StartAt);
        var windowEnd = occurrences.Max(x => x.EndAt);
        var conflicts = await context.StaffWorkAssignments
            .AsNoTracking()
            .Where(x => x.StaffUserId == staffUserId
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced
                && x.StartAt < windowEnd
                && windowStart < x.EndAt)
            .Select(x => new { x.StartAt, x.EndAt })
            .ToListAsync(cancellationToken);

        var conflictedOccurrence = occurrences.FirstOrDefault(occurrence =>
            conflicts.Any(conflict => conflict.StartAt < occurrence.EndAt && occurrence.StartAt < conflict.EndAt));
        if (conflictedOccurrence != default)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateBulkStaffWorkAssignmentsCommand.StaffUserId),
                $"Staff này đã có ca làm trùng thời gian vào {conflictedOccurrence.StartAt.ToOffset(VietnamOffset):dd/MM/yyyy HH:mm}.")]);
        }
    }

    public static StaffWorkAssignment CreateAssignment(
        Guid staffUserId,
        ResolvedAssignmentTarget target,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Guid assignedByUserId,
        DateTimeOffset assignedAt,
        string? dutyRole,
        string? note) =>
        new()
        {
            StaffUserId = staffUserId,
            AssignmentType = target.AssignmentType,
            BoatId = target.Boat?.Id,
            StationId = target.Station?.Id,
            TripStopId = target.TripStop?.Id,
            WorkingDate = ResolveWorkingDate(startAt),
            StartAt = startAt,
            EndAt = endAt,
            DutyRole = string.IsNullOrWhiteSpace(dutyRole) ? null : dutyRole.Trim(),
            Status = StaffWorkAssignmentStatus.Scheduled,
            AssignedByUserId = assignedByUserId,
            AssignedAt = assignedAt,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };

    public static IReadOnlyList<(DateTimeOffset StartAt, DateTimeOffset EndAt)> BuildRecurringShiftOccurrences(
        DateOnly fromDate,
        DateOnly toDate,
        TimeOnly startTime,
        TimeOnly endTime,
        IReadOnlyCollection<int>? daysOfWeek)
    {
        var selectedDays = NormalizeDaysOfWeek(daysOfWeek);
        var occurrences = new List<(DateTimeOffset StartAt, DateTimeOffset EndAt)>();

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            if (selectedDays.Count > 0 && !selectedDays.Contains(ToIsoDayOfWeek(date.DayOfWeek)))
            {
                continue;
            }

            var startAt = new DateTimeOffset(date.ToDateTime(startTime), VietnamOffset).ToUniversalTime();
            var endDate = endTime <= startTime ? date.AddDays(1) : date;
            var endAt = new DateTimeOffset(endDate.ToDateTime(endTime), VietnamOffset).ToUniversalTime();
            EnsureValidTimeRange(startAt, endAt);
            occurrences.Add((startAt, endAt));
        }

        return occurrences;
    }

    public static string AppendReplacementNote(
        string? currentNote,
        string replacementStaffName,
        string? reason)
    {
        var replacementText = string.IsNullOrWhiteSpace(reason)
            ? $"Đã thay thế bởi {replacementStaffName}."
            : $"Đã thay thế bởi {replacementStaffName}. Lý do: {reason.Trim()}";

        return string.IsNullOrWhiteSpace(currentNote)
            ? replacementText
            : $"{currentNote.Trim()} {replacementText}";
    }

    public static IQueryable<StaffWorkAssignment> BuildDtoQuery(IApplicationDbContext context) =>
        context.StaffWorkAssignments
            .AsNoTracking()
            .Include(x => x.StaffUser)
            .Include(x => x.AssignedByUser)
            .Include(x => x.Boat)
            .Include(x => x.Station)
            .Include(x => x.TripStop)
                .ThenInclude(x => x!.Trip);

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
            assignment.TripStop is null
                ? null
                : new StaffWorkAssignmentTripStopDto(
                    assignment.TripStop.Id,
                    assignment.TripStop.TripId,
                    assignment.TripStop.Trip.TripCode,
                    assignment.TripStop.StopOrder,
                    assignment.TripStop.PlannedArrivalTime,
                    assignment.TripStop.PlannedDepartureTime),
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

        if (assignment.Status == StaffWorkAssignmentStatus.Replaced)
        {
            return "Replaced";
        }

        if (now > assignment.EndAt)
        {
            return "Completed";
        }

        if (assignment.StartAt <= now && assignment.EndAt >= now)
        {
            return "Active";
        }

        return "Upcoming";
    }

    private static async Task<ResolvedAssignmentTarget> ResolveBoatTargetAsync(
        IApplicationDbContext context,
        Guid? boatId,
        Guid? stationId,
        Guid? tripStopId,
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

        if (tripStopId.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.TripStopId),
                "Không gửi tripStopId khi assignmentType = Boat.")]);
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
            null,
            startAt.Value.ToUniversalTime(),
            endAt.Value.ToUniversalTime());
    }

    private static async Task<ResolvedAssignmentTarget> ResolveStationTargetAsync(
        IApplicationDbContext context,
        Guid? boatId,
        Guid? stationId,
        Guid? tripStopId,
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

        TripStop? tripStop = null;
        if (tripStopId.HasValue)
        {
            tripStop = await context.Set<TripStop>()
                .Include(x => x.Station)
                .Include(x => x.Trip)
                .SingleOrDefaultAsync(x => x.Id == tripStopId.Value, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy trip stop.");

            if (stationId.HasValue && stationId.Value != tripStop.StationId)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(CreateStaffWorkAssignmentCommand.StationId),
                    "stationId phải khớp với bến của tripStopId.")]);
            }

            stationId = tripStop.StationId;
        }

        if (!stationId.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.StationId),
                "stationId hoặc tripStopId là bắt buộc khi assignmentType = Station.")]);
        }

        var station = tripStop?.Station;
        if (station is null)
        {
            station = await context.Set<Station>()
                .SingleOrDefaultAsync(x => x.Id == stationId.Value, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy bến.");
        }

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
            tripStop,
            startAt.Value.ToUniversalTime(),
            endAt.Value.ToUniversalTime());
    }

    public static void EnsureValidTimeRange(DateTimeOffset startAt, DateTimeOffset endAt)
    {
        if (endAt <= startAt)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.EndAt),
                "endAt phải lớn hơn startAt.")]);
        }

        if (endAt - startAt > MaxSingleAssignmentDuration)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateStaffWorkAssignmentCommand.EndAt),
                "Một ca lẻ không được kéo dài quá 24 giờ. Nếu muốn tạo lịch nhiều ngày, hãy dùng API bulk/recurring.")]);
        }
    }

    private static IReadOnlySet<int> NormalizeDaysOfWeek(IReadOnlyCollection<int>? daysOfWeek)
    {
        if (daysOfWeek is null || daysOfWeek.Count == 0)
        {
            return new HashSet<int>();
        }

        var normalized = new HashSet<int>();
        foreach (var day in daysOfWeek)
        {
            if (day is < 1 or > 7)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(CreateBulkStaffWorkAssignmentsCommand.DaysOfWeek),
                    "daysOfWeek chỉ nhận 1-7, trong đó 1 là Thứ 2 và 7 là Chủ nhật.")]);
            }

            normalized.Add(day);
        }

        return normalized;
    }

    private static int ToIsoDayOfWeek(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;
}
