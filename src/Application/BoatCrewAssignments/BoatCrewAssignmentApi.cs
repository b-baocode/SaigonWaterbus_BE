using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.BoatStaffAssignments;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.BoatCrewAssignments;

public sealed record BoatCrewAssignmentDto(
    Guid AssignmentId,
    Guid BoatId,
    string BoatName,
    Guid StaffUserId,
    string StaffName,
    CrewRole CrewRole,
    DateOnly FromDate,
    DateOnly? ToDate,
    bool IsActive,
    Guid AssignedByUserId,
    string AssignedByName,
    DateTimeOffset AssignedAt);

public sealed record BoatCrewReplacementDto(
    Guid ReplacementId,
    Guid BoatId,
    string BoatName,
    CrewRole CrewRole,
    Guid ReplacedStaffUserId,
    string ReplacedStaffName,
    Guid ReplacementStaffUserId,
    string ReplacementStaffName,
    DateOnly FromDate,
    DateOnly ToDate,
    string Reason,
    bool IsActive,
    Guid AssignedByUserId,
    string AssignedByName,
    DateTimeOffset AssignedAt);

public sealed record BoatCrewCalendarDayDto(
    DateOnly WorkingDate,
    IReadOnlyList<BoatCrewCalendarRoleDto> Crew);

public sealed record BoatCrewCalendarRoleDto(
    CrewRole CrewRole,
    Guid StaffUserId,
    string StaffName,
    bool IsReplacement,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? ReplacedStaffUserId,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ReplacedStaffName,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? ReplacementId);

[Authorize(Roles = "Admin,Manager,Staff")]
public sealed record GetBoatCrewAssignmentsQuery(
    Guid BoatId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    bool ActiveOnly = true) : IRequest<IReadOnlyList<BoatCrewAssignmentDto>>;

public sealed class GetBoatCrewAssignmentsQueryValidator
    : AbstractValidator<GetBoatCrewAssignmentsQuery>
{
    public GetBoatCrewAssignmentsQueryValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x)
            .Must(x => !x.FromDate.HasValue || !x.ToDate.HasValue || x.ToDate.Value >= x.FromDate.Value)
            .WithMessage("toDate phải lớn hơn hoặc bằng fromDate.")
            .OverridePropertyName(nameof(GetBoatCrewAssignmentsQuery.ToDate));
    }
}

public sealed class GetBoatCrewAssignmentsQueryHandler
    : IRequestHandler<GetBoatCrewAssignmentsQuery, IReadOnlyList<BoatCrewAssignmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBoatCrewAssignmentsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BoatCrewAssignmentDto>> Handle(
        GetBoatCrewAssignmentsQuery request,
        CancellationToken cancellationToken)
    {
        await BoatStaffAssignmentSupport.EnsureCurrentUserCanViewBoatStaffAsync(
            _context,
            _userContext,
            cancellationToken);

        var query = BoatCrewAssignmentSupport.BuildBaseAssignmentQuery(_context)
            .Where(x => x.BoatId == request.BoatId);
        query = BoatCrewAssignmentSupport.ApplyDateRange(query, request.FromDate, request.ToDate);

        if (request.ActiveOnly)
        {
            query = query.Where(x => x.IsActive);
        }

        var assignments = await query
            .OrderBy(x => x.CrewRole)
            .ThenBy(x => x.FromDate)
            .ThenBy(x => x.StaffUser.FullName)
            .ToListAsync(cancellationToken);

        return assignments.Select(BoatCrewAssignmentSupport.ToDto).ToList();
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record CreateBoatCrewAssignmentCommand(
    Guid BoatId,
    Guid StaffUserId,
    CrewRole CrewRole,
    DateOnly FromDate,
    DateOnly? ToDate = null) : IRequest<BoatCrewAssignmentDto>;

public sealed class CreateBoatCrewAssignmentCommandValidator
    : AbstractValidator<CreateBoatCrewAssignmentCommand>
{
    public CreateBoatCrewAssignmentCommandValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x.StaffUserId).NotEmpty();
        RuleFor(x => x.CrewRole).IsInEnum();
        RuleFor(x => x.FromDate).NotEmpty();
        RuleFor(x => x)
            .Must(x => !x.ToDate.HasValue || x.ToDate.Value >= x.FromDate)
            .WithMessage("toDate phải lớn hơn hoặc bằng fromDate.")
            .OverridePropertyName(nameof(CreateBoatCrewAssignmentCommand.ToDate));
    }
}

public sealed class CreateBoatCrewAssignmentCommandHandler
    : IRequestHandler<CreateBoatCrewAssignmentCommand, BoatCrewAssignmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CreateBoatCrewAssignmentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<BoatCrewAssignmentDto> Handle(
        CreateBoatCrewAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await BoatStaffAssignmentSupport.EnsureCurrentUserCanManageBoatStaffAsync(
            _context,
            _userContext,
            cancellationToken);

        var boat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tàu.");
        if (boat.Status == BoatStatus.Retired)
        {
            throw AuthSupport.CreateValidationException(nameof(request.BoatId), "Không thể phân crew cho tàu đã Retired.");
        }

        await BoatStaffAssignmentSupport.EnsureStaffCanBeAssignedAsync(
            _context,
            request.StaffUserId,
            nameof(request.StaffUserId),
            cancellationToken);
        await BoatCrewAssignmentSupport.EnsureCrewRoleAvailableAsync(
            _context,
            request.BoatId,
            request.CrewRole,
            request.FromDate,
            request.ToDate,
            null,
            cancellationToken);
        await BoatCrewAssignmentSupport.EnsureStaffHasNoCrewConflictAsync(
            _context,
            request.StaffUserId,
            request.FromDate,
            request.ToDate,
            null,
            cancellationToken);

        var assignment = new BoatCrewAssignment
        {
            BoatId = boat.Id,
            Boat = boat,
            StaffUserId = request.StaffUserId,
            CrewRole = request.CrewRole,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            IsActive = true,
            AssignedByUserId = actor.Id,
            AssignedByUser = actor,
            AssignedAt = _timeProvider.GetUtcNow()
        };

        _context.BoatCrewAssignments.Add(assignment);
        await _context.SaveChangesAsync(cancellationToken);

        return await BoatCrewAssignmentSupport.LoadAssignmentDtoAsync(_context, assignment.Id, cancellationToken);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record DeleteBoatCrewAssignmentCommand(Guid BoatId, Guid AssignmentId) : IRequest;

public sealed class DeleteBoatCrewAssignmentCommandValidator
    : AbstractValidator<DeleteBoatCrewAssignmentCommand>
{
    public DeleteBoatCrewAssignmentCommandValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x.AssignmentId).NotEmpty();
    }
}

public sealed class DeleteBoatCrewAssignmentCommandHandler
    : IRequestHandler<DeleteBoatCrewAssignmentCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DeleteBoatCrewAssignmentCommandHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task Handle(DeleteBoatCrewAssignmentCommand request, CancellationToken cancellationToken)
    {
        await BoatStaffAssignmentSupport.EnsureCurrentUserCanManageBoatStaffAsync(
            _context,
            _userContext,
            cancellationToken);

        var assignment = await _context.BoatCrewAssignments
            .SingleOrDefaultAsync(
                x => x.Id == request.AssignmentId
                    && x.BoatId == request.BoatId
                    && x.ReplacesAssignmentId == null,
                cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phân công crew.");

        assignment.IsActive = false;

        var replacements = await _context.BoatCrewAssignments
            .Where(x => x.ReplacesAssignmentId == assignment.Id && x.IsActive)
            .ToListAsync(cancellationToken);
        foreach (var replacement in replacements)
        {
            replacement.IsActive = false;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}

[Authorize(Roles = "Admin,Manager,Staff")]
public sealed record GetBoatCrewReplacementsQuery(
    Guid BoatId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    bool ActiveOnly = true) : IRequest<IReadOnlyList<BoatCrewReplacementDto>>;

public sealed class GetBoatCrewReplacementsQueryValidator
    : AbstractValidator<GetBoatCrewReplacementsQuery>
{
    public GetBoatCrewReplacementsQueryValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x)
            .Must(x => !x.FromDate.HasValue || !x.ToDate.HasValue || x.ToDate.Value >= x.FromDate.Value)
            .WithMessage("toDate phải lớn hơn hoặc bằng fromDate.")
            .OverridePropertyName(nameof(GetBoatCrewReplacementsQuery.ToDate));
    }
}

public sealed class GetBoatCrewReplacementsQueryHandler
    : IRequestHandler<GetBoatCrewReplacementsQuery, IReadOnlyList<BoatCrewReplacementDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBoatCrewReplacementsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BoatCrewReplacementDto>> Handle(
        GetBoatCrewReplacementsQuery request,
        CancellationToken cancellationToken)
    {
        await BoatStaffAssignmentSupport.EnsureCurrentUserCanViewBoatStaffAsync(
            _context,
            _userContext,
            cancellationToken);

        var query = BoatCrewAssignmentSupport.BuildReplacementQuery(_context)
            .Where(x => x.BoatId == request.BoatId);
        query = BoatCrewAssignmentSupport.ApplyDateRange(query, request.FromDate, request.ToDate);

        if (request.ActiveOnly)
        {
            query = query.Where(x => x.IsActive);
        }

        var replacements = await query
            .OrderBy(x => x.FromDate)
            .ThenBy(x => x.CrewRole)
            .ThenBy(x => x.StaffUser.FullName)
            .ToListAsync(cancellationToken);

        return replacements.Select(BoatCrewAssignmentSupport.ToReplacementDto).ToList();
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record CreateBoatCrewReplacementCommand(
    Guid BoatId,
    CrewRole CrewRole,
    Guid ReplacedStaffUserId,
    Guid ReplacementStaffUserId,
    DateOnly FromDate,
    DateOnly ToDate,
    string Reason) : IRequest<BoatCrewReplacementDto>;

public sealed class CreateBoatCrewReplacementCommandValidator
    : AbstractValidator<CreateBoatCrewReplacementCommand>
{
    public CreateBoatCrewReplacementCommandValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x.CrewRole).IsInEnum();
        RuleFor(x => x.ReplacedStaffUserId).NotEmpty();
        RuleFor(x => x.ReplacementStaffUserId).NotEmpty();
        RuleFor(x => x.FromDate).NotEmpty();
        RuleFor(x => x.ToDate).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.ToDate >= x.FromDate)
            .WithMessage("toDate phải lớn hơn hoặc bằng fromDate.")
            .OverridePropertyName(nameof(CreateBoatCrewReplacementCommand.ToDate));
        RuleFor(x => x)
            .Must(x => x.ReplacedStaffUserId != x.ReplacementStaffUserId)
            .WithMessage("Người thay thế phải khác người được thay.")
            .OverridePropertyName(nameof(CreateBoatCrewReplacementCommand.ReplacementStaffUserId));
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class CreateBoatCrewReplacementCommandHandler
    : IRequestHandler<CreateBoatCrewReplacementCommand, BoatCrewReplacementDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CreateBoatCrewReplacementCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<BoatCrewReplacementDto> Handle(
        CreateBoatCrewReplacementCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await BoatStaffAssignmentSupport.EnsureCurrentUserCanManageBoatStaffAsync(
            _context,
            _userContext,
            cancellationToken);

        var boat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tàu.");
        if (boat.Status == BoatStatus.Retired)
        {
            throw AuthSupport.CreateValidationException(nameof(request.BoatId), "Không thể phân crew cho tàu đã Retired.");
        }

        var baseAssignment = await BoatCrewAssignmentSupport.LoadBaseCrewForReplacementAsync(
            _context,
            request.BoatId,
            request.CrewRole,
            request.ReplacedStaffUserId,
            request.FromDate,
            request.ToDate,
            cancellationToken);
        await BoatStaffAssignmentSupport.EnsureStaffCanBeAssignedAsync(
            _context,
            request.ReplacementStaffUserId,
            nameof(request.ReplacementStaffUserId),
            cancellationToken);
        await BoatCrewAssignmentSupport.EnsureStaffHasNoCrewConflictAsync(
            _context,
            request.ReplacementStaffUserId,
            request.FromDate,
            request.ToDate,
            null,
            cancellationToken);
        await BoatCrewAssignmentSupport.EnsureReplacementAvailableAsync(
            _context,
            baseAssignment.Id,
            request.FromDate,
            request.ToDate,
            null,
            cancellationToken);

        var replacement = new BoatCrewAssignment
        {
            BoatId = request.BoatId,
            CrewRole = request.CrewRole,
            StaffUserId = request.ReplacementStaffUserId,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            ReplacesAssignmentId = baseAssignment.Id,
            ReplacementReason = request.Reason.Trim(),
            IsActive = true,
            AssignedByUserId = actor.Id,
            AssignedByUser = actor,
            AssignedAt = _timeProvider.GetUtcNow()
        };

        _context.BoatCrewAssignments.Add(replacement);
        await _context.SaveChangesAsync(cancellationToken);

        return await BoatCrewAssignmentSupport.LoadReplacementDtoAsync(_context, replacement.Id, cancellationToken);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record DeleteBoatCrewReplacementCommand(Guid BoatId, Guid ReplacementId) : IRequest;

public sealed class DeleteBoatCrewReplacementCommandValidator
    : AbstractValidator<DeleteBoatCrewReplacementCommand>
{
    public DeleteBoatCrewReplacementCommandValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x.ReplacementId).NotEmpty();
    }
}

public sealed class DeleteBoatCrewReplacementCommandHandler
    : IRequestHandler<DeleteBoatCrewReplacementCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DeleteBoatCrewReplacementCommandHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task Handle(DeleteBoatCrewReplacementCommand request, CancellationToken cancellationToken)
    {
        await BoatStaffAssignmentSupport.EnsureCurrentUserCanManageBoatStaffAsync(
            _context,
            _userContext,
            cancellationToken);

        var replacement = await _context.BoatCrewAssignments
            .SingleOrDefaultAsync(
                x => x.Id == request.ReplacementId
                    && x.BoatId == request.BoatId
                    && x.ReplacesAssignmentId != null,
                cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy lịch thay thế crew.");

        replacement.IsActive = false;
        await _context.SaveChangesAsync(cancellationToken);
    }
}

[Authorize(Roles = "Admin,Manager,Staff")]
public sealed record GetBoatCrewCalendarQuery(
    Guid BoatId,
    DateOnly FromDate,
    DateOnly ToDate) : IRequest<IReadOnlyList<BoatCrewCalendarDayDto>>;

public sealed class GetBoatCrewCalendarQueryValidator : AbstractValidator<GetBoatCrewCalendarQuery>
{
    public GetBoatCrewCalendarQueryValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x.FromDate).NotEmpty();
        RuleFor(x => x.ToDate).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.ToDate >= x.FromDate)
            .WithMessage("toDate phải lớn hơn hoặc bằng fromDate.")
            .OverridePropertyName(nameof(GetBoatCrewCalendarQuery.ToDate));
        RuleFor(x => x)
            .Must(x => x.FromDate.AddDays(366) >= x.ToDate)
            .WithMessage("Khoảng xem lịch không được vượt quá 366 ngày.")
            .OverridePropertyName(nameof(GetBoatCrewCalendarQuery.ToDate));
    }
}

public sealed class GetBoatCrewCalendarQueryHandler
    : IRequestHandler<GetBoatCrewCalendarQuery, IReadOnlyList<BoatCrewCalendarDayDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBoatCrewCalendarQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BoatCrewCalendarDayDto>> Handle(
        GetBoatCrewCalendarQuery request,
        CancellationToken cancellationToken)
    {
        await BoatStaffAssignmentSupport.EnsureCurrentUserCanViewBoatStaffAsync(
            _context,
            _userContext,
            cancellationToken);

        var assignments = await BoatCrewAssignmentSupport.ApplyDateRange(
                BoatCrewAssignmentSupport.BuildBaseAssignmentQuery(_context)
                    .Where(x => x.BoatId == request.BoatId && x.IsActive),
                request.FromDate,
                request.ToDate)
            .ToListAsync(cancellationToken);
        var replacements = await BoatCrewAssignmentSupport.ApplyDateRange(
                BoatCrewAssignmentSupport.BuildReplacementQuery(_context)
                    .Where(x => x.BoatId == request.BoatId && x.IsActive),
                request.FromDate,
                request.ToDate)
            .ToListAsync(cancellationToken);

        var result = new List<BoatCrewCalendarDayDto>();
        for (var date = request.FromDate; date <= request.ToDate; date = date.AddDays(1))
        {
            var crew = assignments
                .Where(x => BoatCrewAssignmentSupport.ContainsDate(x.FromDate, x.ToDate, date))
                .OrderBy(x => x.CrewRole)
                .ThenBy(x => x.StaffUser.FullName)
                .Select(x => BoatCrewAssignmentSupport.ToCalendarRoleDto(
                    x,
                    replacements
                        .Where(r => r.ReplacesAssignmentId == x.Id
                            && r.ToDate.HasValue
                            && BoatCrewAssignmentSupport.ContainsDate(r.FromDate, r.ToDate.Value, date))
                        .OrderByDescending(r => r.AssignedAt)
                        .FirstOrDefault()))
                .ToArray();

            result.Add(new BoatCrewCalendarDayDto(date, crew));
        }

        return result;
    }
}

internal static class BoatCrewAssignmentSupport
{
    public static IQueryable<BoatCrewAssignment> BuildBaseAssignmentQuery(IApplicationDbContext context) =>
        context.BoatCrewAssignments
            .AsNoTracking()
            .Where(x => x.ReplacesAssignmentId == null)
            .Include(x => x.Boat)
            .Include(x => x.StaffUser)
            .Include(x => x.AssignedByUser);

    public static IQueryable<BoatCrewAssignment> BuildReplacementQuery(IApplicationDbContext context) =>
        context.BoatCrewAssignments
            .AsNoTracking()
            .Where(x => x.ReplacesAssignmentId != null)
            .Include(x => x.Boat)
            .Include(x => x.StaffUser)
            .Include(x => x.AssignedByUser)
            .Include(x => x.ReplacesAssignment!)
            .ThenInclude(x => x.StaffUser);

    public static IQueryable<BoatCrewAssignment> ApplyDateRange(
        IQueryable<BoatCrewAssignment> query,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        if (fromDate.HasValue)
        {
            query = query.Where(x => !x.ToDate.HasValue || x.ToDate.Value >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(x => x.FromDate <= toDate.Value);
        }

        return query;
    }

    public static bool ContainsDate(DateOnly fromDate, DateOnly? toDate, DateOnly date) =>
        fromDate <= date && (!toDate.HasValue || toDate.Value >= date);

    public static bool ContainsDate(DateOnly fromDate, DateOnly toDate, DateOnly date) =>
        fromDate <= date && toDate >= date;

    public static async Task EnsureCrewRoleAvailableAsync(
        IApplicationDbContext context,
        Guid boatId,
        CrewRole crewRole,
        DateOnly fromDate,
        DateOnly? toDate,
        Guid? excludingAssignmentId,
        CancellationToken cancellationToken)
    {
        var hasConflict = await context.BoatCrewAssignments.AnyAsync(
            x => x.BoatId == boatId
                && x.CrewRole == crewRole
                && x.ReplacesAssignmentId == null
                && x.IsActive
                && (!excludingAssignmentId.HasValue || x.Id != excludingAssignmentId.Value)
                && (!x.ToDate.HasValue || x.ToDate.Value >= fromDate)
                && (!toDate.HasValue || x.FromDate <= toDate.Value),
            cancellationToken);

        if (hasConflict)
        {
            throw new ValidationException([new ValidationFailure(
                "crewRole",
                "Tàu đã có crew active cùng vai trò trong khoảng ngày này.")]);
        }
    }

    public static async Task EnsureStaffHasNoCrewConflictAsync(
        IApplicationDbContext context,
        Guid staffUserId,
        DateOnly fromDate,
        DateOnly? toDate,
        Guid? excludingAssignmentId,
        CancellationToken cancellationToken)
    {
        var hasConflict = await context.BoatCrewAssignments.AnyAsync(
            x => x.StaffUserId == staffUserId
                && x.IsActive
                && (!excludingAssignmentId.HasValue || x.Id != excludingAssignmentId.Value)
                && (!x.ToDate.HasValue || x.ToDate.Value >= fromDate)
                && (!toDate.HasValue || x.FromDate <= toDate.Value),
            cancellationToken);
        if (hasConflict)
        {
            throw new ValidationException([new ValidationFailure(
                "staffUserId",
                "Staff này đã được gắn crew tàu khác trong khoảng ngày này.")]);
        }
    }

    public static async Task<BoatCrewAssignment> LoadBaseCrewForReplacementAsync(
        IApplicationDbContext context,
        Guid boatId,
        CrewRole crewRole,
        Guid replacedStaffUserId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var assignment = await context.BoatCrewAssignments
            .FirstOrDefaultAsync(
                x => x.BoatId == boatId
                    && x.CrewRole == crewRole
                    && x.StaffUserId == replacedStaffUserId
                    && x.ReplacesAssignmentId == null
                    && x.IsActive
                    && x.FromDate <= fromDate
                    && (!x.ToDate.HasValue || x.ToDate.Value >= toDate),
                cancellationToken);
        if (assignment is null)
        {
            throw new ValidationException([new ValidationFailure(
                "replacedStaffUserId",
                "Người được thay phải là crew mặc định của tàu trong toàn bộ khoảng ngày thay thế.")]);
        }

        return assignment;
    }

    public static async Task EnsureReplacementAvailableAsync(
        IApplicationDbContext context,
        Guid baseAssignmentId,
        DateOnly fromDate,
        DateOnly toDate,
        Guid? excludingReplacementId,
        CancellationToken cancellationToken)
    {
        var hasConflict = await context.BoatCrewAssignments.AnyAsync(
            x => x.ReplacesAssignmentId == baseAssignmentId
                && x.IsActive
                && (!excludingReplacementId.HasValue || x.Id != excludingReplacementId.Value)
                && x.ToDate.HasValue
                && x.ToDate.Value >= fromDate
                && x.FromDate <= toDate,
            cancellationToken);
        if (hasConflict)
        {
            throw new ValidationException([new ValidationFailure(
                "fromDate",
                "Khoảng ngày này đã có người thay thế cho crew được chọn.")]);
        }
    }

    public static async Task<BoatCrewAssignmentDto> LoadAssignmentDtoAsync(
        IApplicationDbContext context,
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var assignment = await BuildBaseAssignmentQuery(context)
            .SingleAsync(x => x.Id == assignmentId, cancellationToken);
        return ToDto(assignment);
    }

    public static async Task<BoatCrewReplacementDto> LoadReplacementDtoAsync(
        IApplicationDbContext context,
        Guid replacementId,
        CancellationToken cancellationToken)
    {
        var replacement = await BuildReplacementQuery(context)
            .SingleAsync(x => x.Id == replacementId, cancellationToken);
        return ToReplacementDto(replacement);
    }

    public static BoatCrewAssignmentDto ToDto(BoatCrewAssignment assignment) =>
        new(
            assignment.Id,
            assignment.BoatId,
            assignment.Boat.Name,
            assignment.StaffUserId,
            assignment.StaffUser.FullName,
            assignment.CrewRole,
            assignment.FromDate,
            assignment.ToDate,
            assignment.IsActive,
            assignment.AssignedByUserId,
            assignment.AssignedByUser.FullName,
            assignment.AssignedAt);

    public static BoatCrewReplacementDto ToReplacementDto(BoatCrewAssignment replacement) =>
        new(
            replacement.Id,
            replacement.BoatId,
            replacement.Boat.Name,
            replacement.CrewRole,
            replacement.ReplacesAssignment!.StaffUserId,
            replacement.ReplacesAssignment.StaffUser.FullName,
            replacement.StaffUserId,
            replacement.StaffUser.FullName,
            replacement.FromDate,
            replacement.ToDate!.Value,
            replacement.ReplacementReason ?? string.Empty,
            replacement.IsActive,
            replacement.AssignedByUserId,
            replacement.AssignedByUser.FullName,
            replacement.AssignedAt);

    public static BoatCrewCalendarRoleDto ToCalendarRoleDto(
        BoatCrewAssignment assignment,
        BoatCrewAssignment? replacement) =>
        replacement is null
            ? new BoatCrewCalendarRoleDto(
                assignment.CrewRole,
                assignment.StaffUserId,
                assignment.StaffUser.FullName,
                false,
                null,
                null,
                null)
            : new BoatCrewCalendarRoleDto(
                assignment.CrewRole,
                replacement.StaffUserId,
                replacement.StaffUser.FullName,
                true,
                assignment.StaffUserId,
                assignment.StaffUser.FullName,
                replacement.Id);
}
