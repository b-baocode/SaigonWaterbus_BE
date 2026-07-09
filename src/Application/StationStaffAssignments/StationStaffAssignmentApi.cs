using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.BoatStaffAssignments;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.StationStaffAssignments;

public sealed record StationStaffAssignmentDto(
    Guid AssignmentId,
    OperationScheduleSourceType SourceType,
    Guid SourceId,
    Guid StationId,
    string StationCode,
    string StationName,
    Guid StaffUserId,
    string StaffName,
    DateOnly WorkingDate,
    string ShiftCode,
    string? DutyRole,
    bool IsActive,
    Guid AssignedByUserId,
    string AssignedByName,
    DateTimeOffset AssignedAt);

[Authorize(Roles = "Admin,Manager,Staff")]
public sealed record GetStationStaffAssignmentsQuery(
    OperationScheduleSourceType? SourceType = null,
    Guid? SourceId = null,
    Guid? StationId = null,
    DateOnly? WorkingDate = null,
    bool ActiveOnly = true) : IRequest<IReadOnlyList<StationStaffAssignmentDto>>;

public sealed class GetStationStaffAssignmentsQueryHandler
    : IRequestHandler<GetStationStaffAssignmentsQuery, IReadOnlyList<StationStaffAssignmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetStationStaffAssignmentsQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<StationStaffAssignmentDto>> Handle(
        GetStationStaffAssignmentsQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var query = BuildAssignmentQuery(_context);

        if (request.SourceType.HasValue)
        {
            query = query.Where(x => x.SourceType == request.SourceType.Value);
        }

        if (request.SourceId.HasValue)
        {
            query = query.Where(x => x.SourceId == request.SourceId.Value);
        }

        if (request.StationId.HasValue)
        {
            query = query.Where(x => x.StationId == request.StationId.Value);
        }

        if (request.WorkingDate.HasValue)
        {
            query = query.Where(x => x.WorkingDate == request.WorkingDate.Value);
        }

        if (request.ActiveOnly)
        {
            query = query.Where(x => x.IsActive);
        }

        if (AuthSupport.IsManager(actor))
        {
            var stationIds = await StationStaffAssignmentSupport.LoadManagedStationIdsAsync(
                _context,
                actor.Id,
                cancellationToken);
            query = query.Where(x => stationIds.Contains(x.StationId));
        }
        else if (AuthSupport.IsStaff(actor))
        {
            query = query.Where(x => x.StaffUserId == actor.Id);
        }
        else if (!AuthSupport.IsAdmin(actor))
        {
            throw new ForbiddenAccessException();
        }

        var assignments = await query
            .OrderBy(x => x.WorkingDate)
            .ThenBy(x => x.Station.StationName)
            .ThenBy(x => x.ShiftCode)
            .ThenBy(x => x.DutyRole)
            .ThenBy(x => x.StaffUser.FullName)
            .ToListAsync(cancellationToken);

        return assignments.Select(StationStaffAssignmentSupport.ToDto).ToList();
    }

    private static IQueryable<StationStaffAssignment> BuildAssignmentQuery(IApplicationDbContext context) =>
        context.StationStaffAssignments
            .AsNoTracking()
            .Include(x => x.Station)
            .Include(x => x.StaffUser)
            .Include(x => x.AssignedByUser);
}

[Authorize(Roles = "Admin,Manager")]
public sealed record AssignStationStaffCommand(
    Guid StationId,
    Guid StaffUserId,
    OperationScheduleSourceType SourceType,
    Guid SourceId,
    DateOnly WorkingDate,
    string? ShiftCode = null,
    string? DutyRole = null) : IRequest<StationStaffAssignmentDto>;

public sealed class AssignStationStaffCommandValidator : AbstractValidator<AssignStationStaffCommand>
{
    public AssignStationStaffCommandValidator()
    {
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.StaffUserId).NotEmpty();
        RuleFor(x => x.SourceType).IsInEnum();
        RuleFor(x => x.SourceId).NotEmpty();
        RuleFor(x => x.WorkingDate).NotEmpty();
        RuleFor(x => x.ShiftCode)
            .MaximumLength(30)
            .Must(BoatStaffAssignmentSupport.IsValidShiftCode)
            .WithMessage("Ca làm việc chỉ được là Day hoặc Evening.");
        RuleFor(x => x.DutyRole).MaximumLength(50);
    }
}

public sealed class AssignStationStaffCommandHandler
    : IRequestHandler<AssignStationStaffCommand, StationStaffAssignmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public AssignStationStaffCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<StationStaffAssignmentDto> Handle(
        AssignStationStaffCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        await StationStaffAssignmentSupport.EnsureCanManageStationAsync(
            _context,
            actor,
            request.StationId,
            cancellationToken);

        var station = await _context.Set<Station>()
            .SingleOrDefaultAsync(x => x.Id == request.StationId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy bến.");
        if (station.Status != StationStatus.Active)
        {
            throw AuthSupport.CreateValidationException(nameof(request.StationId), "Không thể phân công staff cho bến không Active.");
        }

        await StationStaffAssignmentSupport.EnsureSourceMatchesStationAsync(
            _context,
            request.SourceType,
            request.SourceId,
            request.StationId,
            request.WorkingDate,
            cancellationToken);
        await StationStaffAssignmentSupport.EnsureGroundStaffCanBeAssignedAsync(
            _context,
            request.StaffUserId,
            request.StationId,
            nameof(request.StaffUserId),
            cancellationToken);

        var shiftCode = BoatStaffAssignmentSupport.NormalizeShiftCode(request.ShiftCode);
        await StationStaffAssignmentSupport.EnsureStaffIsAvailableAsync(
            _context,
            request.StaffUserId,
            request.WorkingDate,
            shiftCode,
            null,
            cancellationToken);

        var assignment = new StationStaffAssignment
        {
            StationId = station.Id,
            Station = station,
            StaffUserId = request.StaffUserId,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            WorkingDate = request.WorkingDate,
            ShiftCode = shiftCode,
            DutyRole = BoatStaffAssignmentSupport.NormalizeOptional(request.DutyRole),
            IsActive = true,
            AssignedByUserId = actor.Id,
            AssignedByUser = actor,
            AssignedAt = _timeProvider.GetUtcNow()
        };

        _context.StationStaffAssignments.Add(assignment);
        await _context.SaveChangesAsync(cancellationToken);

        return await StationStaffAssignmentSupport.LoadDtoAsync(
            _context,
            assignment.Id,
            cancellationToken);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record DeactivateStationStaffAssignmentCommand(Guid AssignmentId) : IRequest;

public sealed class DeactivateStationStaffAssignmentCommandValidator
    : AbstractValidator<DeactivateStationStaffAssignmentCommand>
{
    public DeactivateStationStaffAssignmentCommandValidator()
    {
        RuleFor(x => x.AssignmentId).NotEmpty();
    }
}

public sealed class DeactivateStationStaffAssignmentCommandHandler
    : IRequestHandler<DeactivateStationStaffAssignmentCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DeactivateStationStaffAssignmentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task Handle(
        DeactivateStationStaffAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var assignment = await _context.StationStaffAssignments
            .SingleOrDefaultAsync(x => x.Id == request.AssignmentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phân công staff mặt đất.");

        await StationStaffAssignmentSupport.EnsureCanManageStationAsync(
            _context,
            actor,
            assignment.StationId,
            cancellationToken);

        assignment.IsActive = false;
        await _context.SaveChangesAsync(cancellationToken);
    }
}

internal static class StationStaffAssignmentSupport
{
    public static async Task<IReadOnlyList<Guid>> LoadManagedStationIdsAsync(
        IApplicationDbContext context,
        Guid managerUserId,
        CancellationToken cancellationToken) =>
        await context.Set<UserStationAssignment>()
            .Where(x => x.UserId == managerUserId && x.IsActive)
            .Select(x => x.StationId)
            .ToListAsync(cancellationToken);

    public static async Task EnsureCanManageStationAsync(
        IApplicationDbContext context,
        User actor,
        Guid stationId,
        CancellationToken cancellationToken)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        if (AuthSupport.IsManager(actor)
            && await context.Set<UserStationAssignment>()
                .AnyAsync(x => x.UserId == actor.Id
                    && x.StationId == stationId
                    && x.IsActive, cancellationToken))
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static async Task EnsureGroundStaffCanBeAssignedAsync(
        IApplicationDbContext context,
        Guid staffUserId,
        Guid stationId,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var staff = await context.Users
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == staffUserId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy staff.");

        if (!string.Equals(staff.Role.SystemName, Roles.StaffSystemName, StringComparison.Ordinal))
        {
            throw new ValidationException([new ValidationFailure(propertyName, "Người được phân công phải có role Staff.")]);
        }

        if (staff.Status != UserStatus.Active)
        {
            throw new ValidationException([new ValidationFailure(propertyName, "Staff phải đang Active để được phân công.")]);
        }

        if (staff.StaffType != StaffType.Ground)
        {
            throw new ValidationException([new ValidationFailure(propertyName, "Chỉ nhân viên mặt đất mới được phân công tại bến.")]);
        }

        var belongsToStation = await context.Set<UserStationAssignment>()
            .AnyAsync(x => x.UserId == staffUserId
                && x.StationId == stationId
                && x.IsActive, cancellationToken);
        if (!belongsToStation)
        {
            throw new ValidationException([new ValidationFailure(propertyName, "Staff chưa thuộc bến này.")]);
        }
    }

    public static async Task EnsureStaffIsAvailableAsync(
        IApplicationDbContext context,
        Guid staffUserId,
        DateOnly workingDate,
        string shiftCode,
        Guid? excludingAssignmentId,
        CancellationToken cancellationToken)
    {
        var hasConflict = await context.StationStaffAssignments.AnyAsync(
            x => x.StaffUserId == staffUserId
              && x.WorkingDate == workingDate
              && x.ShiftCode == shiftCode
              && x.IsActive
              && (!excludingAssignmentId.HasValue || x.Id != excludingAssignmentId.Value),
            cancellationToken);

        if (hasConflict)
        {
            throw new ValidationException([new ValidationFailure(
                "staffUserId",
                "Staff này đã được phân công mặt đất trong cùng ngày/ca.")]);
        }
    }

    public static async Task EnsureSourceMatchesStationAsync(
        IApplicationDbContext context,
        OperationScheduleSourceType sourceType,
        Guid sourceId,
        Guid stationId,
        DateOnly workingDate,
        CancellationToken cancellationToken)
    {
        switch (sourceType)
        {
            case OperationScheduleSourceType.CharterBooking:
                await EnsureCharterBookingMatchesStationAsync(
                    context,
                    sourceId,
                    stationId,
                    workingDate,
                    cancellationToken);
                return;

            case OperationScheduleSourceType.RegularTrip:
                await EnsureRegularTripMatchesStationAsync(
                    context,
                    sourceId,
                    stationId,
                    workingDate,
                    cancellationToken);
                return;

            default:
                throw AuthSupport.CreateValidationException(nameof(sourceType), "SourceType không hợp lệ.");
        }
    }

    public static async Task<StationStaffAssignmentDto> LoadDtoAsync(
        IApplicationDbContext context,
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var assignment = await context.StationStaffAssignments
            .AsNoTracking()
            .Include(x => x.Station)
            .Include(x => x.StaffUser)
            .Include(x => x.AssignedByUser)
            .SingleAsync(x => x.Id == assignmentId, cancellationToken);

        return ToDto(assignment);
    }

    public static StationStaffAssignmentDto ToDto(StationStaffAssignment assignment) =>
        new(
            assignment.Id,
            assignment.SourceType,
            assignment.SourceId,
            assignment.StationId,
            assignment.Station.StationCode,
            assignment.Station.StationName,
            assignment.StaffUserId,
            assignment.StaffUser.FullName,
            assignment.WorkingDate,
            assignment.ShiftCode ?? BoatStaffAssignmentSupport.DefaultShiftCode,
            assignment.DutyRole,
            assignment.IsActive,
            assignment.AssignedByUserId,
            assignment.AssignedByUser.FullName,
            assignment.AssignedAt);

    private static async Task EnsureCharterBookingMatchesStationAsync(
        IApplicationDbContext context,
        Guid sourceId,
        Guid stationId,
        DateOnly workingDate,
        CancellationToken cancellationToken)
    {
        var booking = await context.Set<Booking>()
            .AsNoTracking()
            .Include(x => x.ItineraryStops)
            .SingleOrDefaultAsync(x => x.Id == sourceId
                && x.BookingType == Booking.CharterBookingType, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy charter booking.");

        if (!booking.DepartureDate.HasValue)
        {
            throw AuthSupport.CreateValidationException(nameof(AssignStationStaffCommand.SourceId), "Charter booking chưa có ngày khởi hành.");
        }

        if (booking.DepartureDate.Value != workingDate)
        {
            throw AuthSupport.CreateValidationException(nameof(AssignStationStaffCommand.WorkingDate), "Ngày làm việc phải trùng ngày khởi hành charter booking.");
        }

        var stationMatches = booking.FromStationId == stationId
            || booking.ToStationId == stationId
            || booking.ItineraryStops.Any(x => x.StationId == stationId);
        if (!stationMatches)
        {
            throw AuthSupport.CreateValidationException(nameof(AssignStationStaffCommand.StationId), "Bến không thuộc lịch trình charter booking.");
        }
    }

    private static async Task EnsureRegularTripMatchesStationAsync(
        IApplicationDbContext context,
        Guid sourceId,
        Guid stationId,
        DateOnly workingDate,
        CancellationToken cancellationToken)
    {
        var trip = await context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
            .SingleOrDefaultAsync(x => x.Id == sourceId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy trip.");

        if (trip.OperatingDate != workingDate)
        {
            throw AuthSupport.CreateValidationException(nameof(AssignStationStaffCommand.WorkingDate), "Ngày làm việc phải trùng ngày vận hành trip.");
        }

        if (!trip.Route.RouteStops.Any(x => x.StationId == stationId))
        {
            throw AuthSupport.CreateValidationException(nameof(AssignStationStaffCommand.StationId), "Bến không thuộc route của trip.");
        }
    }
}
