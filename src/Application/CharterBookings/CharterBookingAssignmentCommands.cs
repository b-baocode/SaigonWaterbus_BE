using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.BoatStaffAssignments;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record AssignCharterBookingManagerCommand(Guid BookingId, Guid? ManagerUserId)
    : IRequest<CharterBookingDetailDto>;

public sealed class AssignCharterBookingManagerCommandValidator
    : AbstractValidator<AssignCharterBookingManagerCommand>
{
    public AssignCharterBookingManagerCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.ManagerUserId).NotEmpty().When(x => x.ManagerUserId.HasValue);
    }
}

public sealed class AssignCharterBookingManagerCommandHandler
    : IRequestHandler<AssignCharterBookingManagerCommand, CharterBookingDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public AssignCharterBookingManagerCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<CharterBookingDetailDto> Handle(
        AssignCharterBookingManagerCommand request,
        CancellationToken cancellationToken)
    {
        await CharterBookingAssignmentSupport.EnsureCurrentUserIsAdminAsync(
            _context,
            _userContext,
            cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildDetailQuery(_context)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (request.ManagerUserId.HasValue)
        {
            var manager = await CharterBookingAssignmentSupport.EnsureAssignableManagerAsync(
                _context,
                request.ManagerUserId.Value,
                nameof(request.ManagerUserId),
                cancellationToken);
            booking.AssignedManagerId = manager.Id;
            booking.AssignedManager = manager;
        }
        else
        {
            booking.AssignedManagerId = null;
            booking.AssignedManager = null;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                request.ManagerUserId.HasValue ? "ManagerAssigned" : "ManagerUnassigned",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus),
            cancellationToken);

        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context,
            booking,
            cancellationToken);

        return CharterBookingQuerySupport.ToDetailDto(booking, relatedRoutes);
    }
}

public sealed record GetAssignedCharterBookingsQuery()
    : IRequest<IReadOnlyList<CharterBookingListItemDto>>;

public sealed class GetAssignedCharterBookingsQueryHandler
    : IRequestHandler<GetAssignedCharterBookingsQuery, IReadOnlyList<CharterBookingListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetAssignedCharterBookingsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<CharterBookingListItemDto>> Handle(
        GetAssignedCharterBookingsQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var query = CharterBookingQuerySupport.BuildBaseQuery(_context)
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.CharterBoats)
            .AsQueryable();

        if (AuthSupport.IsManager(actor))
        {
            query = query.Where(x => x.AssignedManagerId == actor.Id);
        }
        else if (AuthSupport.IsStaff(actor))
        {
            query = query.Where(x => x.DepartureDate.HasValue
                && ((x.BoatId.HasValue && _context.BoatStaffAssignments.Any(a =>
                        a.StaffUserId == actor.Id
                        && a.BoatId == x.BoatId.Value
                        && a.WorkingDate == x.DepartureDate.Value
                        && a.IsActive))
                    || x.CharterBoats.Any(cb => _context.BoatStaffAssignments.Any(a =>
                        a.StaffUserId == actor.Id
                        && a.BoatId == cb.BoatId
                        && a.WorkingDate == x.DepartureDate.Value
                        && a.IsActive))));
        }
        else if (!AuthSupport.IsAdmin(actor))
        {
            throw new ForbiddenAccessException();
        }

        var bookings = await query
            .OrderByDescending(x => x.Created)
            .ToListAsync(cancellationToken);

        return bookings.Select(CharterBookingListItemMapper.ToDto).ToList();
    }
}

public sealed record GetAssignedCharterBookingDetailQuery(Guid BookingId) : IRequest<CharterBookingDetailDto>;

public sealed class GetAssignedCharterBookingDetailQueryHandler
    : IRequestHandler<GetAssignedCharterBookingDetailQuery, CharterBookingDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetAssignedCharterBookingDetailQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CharterBookingDetailDto> Handle(
        GetAssignedCharterBookingDetailQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var booking = await CharterBookingQuerySupport.BuildDetailQuery(_context)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        await CharterBookingAssignmentSupport.EnsureCanViewOperationalAsync(
            _context,
            actor,
            booking,
            includeCustomerOwner: false,
            notFoundWhenDenied: true,
            cancellationToken);

        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context,
            booking,
            cancellationToken);

        return CharterBookingQuerySupport.ToDetailDto(booking, relatedRoutes);
    }
}

public sealed record GetCharterBookingStaffAssignmentsQuery(Guid BookingId, bool ActiveOnly = true)
    : IRequest<IReadOnlyList<CharterBookingStaffAssignmentDto>>;

public sealed class GetCharterBookingStaffAssignmentsQueryHandler
    : IRequestHandler<GetCharterBookingStaffAssignmentsQuery, IReadOnlyList<CharterBookingStaffAssignmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCharterBookingStaffAssignmentsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<CharterBookingStaffAssignmentDto>> Handle(
        GetCharterBookingStaffAssignmentsQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var booking = await LoadBookingForAssignmentAsync(request.BookingId, cancellationToken);

        await CharterBookingAssignmentSupport.EnsureCanViewOperationalAsync(
            _context,
            actor,
            booking,
            includeCustomerOwner: false,
            notFoundWhenDenied: true,
            cancellationToken);

        return await CharterBookingAssignmentSupport.LoadStaffAssignmentsAsync(
            _context,
            booking,
            request.ActiveOnly,
            cancellationToken);
    }

    private Task<Booking> LoadBookingForAssignmentAsync(Guid bookingId, CancellationToken cancellationToken) =>
        CharterBookingAssignmentCommandSupport.LoadBookingForAssignmentAsync(_context, bookingId, cancellationToken);
}

public sealed record AssignCharterBookingStaffCommand(
    Guid BookingId,
    Guid StaffUserId,
    Guid? BoatId = null,
    string? ShiftCode = null) : IRequest<CharterBookingStaffAssignmentDto>;

public sealed class AssignCharterBookingStaffCommandValidator
    : AbstractValidator<AssignCharterBookingStaffCommand>
{
    public AssignCharterBookingStaffCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.StaffUserId).NotEmpty();
        RuleFor(x => x.BoatId).NotEmpty().When(x => x.BoatId.HasValue);
        RuleFor(x => x.ShiftCode)
            .MaximumLength(30)
            .Must(BoatStaffAssignmentSupport.IsValidShiftCode)
            .WithMessage("Ca làm việc chỉ được là Day hoặc Evening.");
    }
}

public sealed class AssignCharterBookingStaffCommandHandler
    : IRequestHandler<AssignCharterBookingStaffCommand, CharterBookingStaffAssignmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public AssignCharterBookingStaffCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<CharterBookingStaffAssignmentDto> Handle(
        AssignCharterBookingStaffCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var booking = await CharterBookingAssignmentCommandSupport.LoadBookingForAssignmentAsync(
            _context,
            request.BookingId,
            cancellationToken);

        CharterBookingAssignmentSupport.EnsureCanManageCharterStaff(actor, booking);

        var boatId = CharterBookingAssignmentCommandSupport.ResolveSelectedBoatId(booking, request.BoatId);
        var boat = await _context.Boats.SingleAsync(x => x.Id == boatId, cancellationToken);
        if (boat.Status == BoatStatus.Retired)
        {
            throw AuthSupport.CreateValidationException(nameof(request.BoatId), "Không thể phân công staff cho tàu đã Retired.");
        }

        var workingDate = CharterBookingAssignmentCommandSupport.ResolveWorkingDate(booking);
        var shiftCode = BoatStaffAssignmentSupport.NormalizeShiftCode(request.ShiftCode);

        await BoatStaffAssignmentSupport.EnsureStaffCanBeAssignedAsync(
            _context,
            request.StaffUserId,
            nameof(request.StaffUserId),
            cancellationToken);
        await BoatStaffAssignmentSupport.EnsureStaffIsAvailableAsync(
            _context,
            request.StaffUserId,
            workingDate,
            shiftCode,
            null,
            cancellationToken);

        var assignment = new BoatStaffAssignment
        {
            BoatId = boat.Id,
            Boat = boat,
            StaffUserId = request.StaffUserId,
            WorkingDate = workingDate,
            ShiftCode = shiftCode,
            DutyRole = BoatStaffAssignmentSupport.OnBoardDutyRole,
            IsActive = true,
            AssignedByUserId = actor.Id,
            AssignedByUser = actor,
            AssignedAt = _timeProvider.GetUtcNow()
        };

        _context.BoatStaffAssignments.Add(assignment);
        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "StaffAssigned",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                assignment.AssignedAt),
            cancellationToken);

        return await CharterBookingAssignmentCommandSupport.LoadStaffAssignmentDtoAsync(
            _context,
            assignment.Id,
            cancellationToken);
    }
}

public sealed record ReplaceCharterBookingStaffCommand(
    Guid BookingId,
    Guid AssignmentId,
    Guid ReplacementStaffUserId,
    string Reason) : IRequest<CharterBookingStaffAssignmentDto>;

public sealed class ReplaceCharterBookingStaffCommandValidator
    : AbstractValidator<ReplaceCharterBookingStaffCommand>
{
    public ReplaceCharterBookingStaffCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.AssignmentId).NotEmpty();
        RuleFor(x => x.ReplacementStaffUserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class ReplaceCharterBookingStaffCommandHandler
    : IRequestHandler<ReplaceCharterBookingStaffCommand, CharterBookingStaffAssignmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public ReplaceCharterBookingStaffCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<CharterBookingStaffAssignmentDto> Handle(
        ReplaceCharterBookingStaffCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var booking = await CharterBookingAssignmentCommandSupport.LoadBookingForAssignmentAsync(
            _context,
            request.BookingId,
            cancellationToken);

        CharterBookingAssignmentSupport.EnsureCanManageCharterStaff(actor, booking);

        var workingDate = CharterBookingAssignmentCommandSupport.ResolveWorkingDate(booking);
        var selectedBoatIds = CharterBookingAssignmentSupport.ResolveSelectedBoatIds(booking);
        var oldAssignment = await _context.BoatStaffAssignments
            .Include(x => x.Boat)
            .Include(x => x.StaffUser)
            .SingleOrDefaultAsync(
                x => x.Id == request.AssignmentId
                    && selectedBoatIds.Contains(x.BoatId)
                    && x.WorkingDate == workingDate,
                cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phân công staff của charter booking.");

        if (!oldAssignment.IsActive)
        {
            throw AuthSupport.CreateValidationException(nameof(request.AssignmentId), "Phân công này đã không còn active.");
        }

        await BoatStaffAssignmentSupport.EnsureStaffCanBeAssignedAsync(
            _context,
            request.ReplacementStaffUserId,
            nameof(request.ReplacementStaffUserId),
            cancellationToken);
        await BoatStaffAssignmentSupport.EnsureStaffIsAvailableAsync(
            _context,
            request.ReplacementStaffUserId,
            workingDate,
            oldAssignment.ShiftCode ?? BoatStaffAssignmentSupport.DefaultShiftCode,
            oldAssignment.Id,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var newAssignment = new BoatStaffAssignment
        {
            BoatId = oldAssignment.BoatId,
            Boat = oldAssignment.Boat,
            StaffUserId = request.ReplacementStaffUserId,
            WorkingDate = workingDate,
            ShiftCode = oldAssignment.ShiftCode,
            DutyRole = BoatStaffAssignmentSupport.OnBoardDutyRole,
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
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "StaffReplaced",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                now),
            cancellationToken);

        return await CharterBookingAssignmentCommandSupport.LoadStaffAssignmentDtoAsync(
            _context,
            newAssignment.Id,
            cancellationToken);
    }
}

internal static class CharterBookingAssignmentCommandSupport
{
    public static async Task<Booking> LoadBookingForAssignmentAsync(
        IApplicationDbContext context,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        return await CharterBookingQuerySupport.BuildBaseQuery(context)
            .Include(x => x.AssignedManager)
            .Include(x => x.Boat)
            .Include(x => x.CharterBoats)
                .ThenInclude(x => x.Boat)
            .SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");
    }

    public static Guid ResolveSelectedBoatId(Booking booking, Guid? requestedBoatId)
    {
        var selectedBoatIds = CharterBookingAssignmentSupport.ResolveSelectedBoatIds(booking);
        if (selectedBoatIds.Count == 0)
        {
            throw new ValidationException([
                new ValidationFailure("boatId", "Charter booking chưa được gán tàu nên chưa thể phân staff.")
            ]);
        }

        if (requestedBoatId.HasValue)
        {
            if (!selectedBoatIds.Contains(requestedBoatId.Value))
            {
                throw new ValidationException([
                    new ValidationFailure("boatId", "boatId không thuộc charter booking.")
                ]);
            }

            return requestedBoatId.Value;
        }

        if (selectedBoatIds.Count > 1)
        {
            throw new ValidationException([
                new ValidationFailure("boatId", "Charter booking có nhiều tàu, cần truyền boatId để phân staff.")
            ]);
        }

        return selectedBoatIds[0];
    }

    public static DateOnly ResolveWorkingDate(Booking booking) =>
        booking.DepartureDate
        ?? throw new ValidationException([
            new ValidationFailure("departureDate", "Charter booking chưa có ngày khởi hành.")
        ]);

    public static async Task<CharterBookingStaffAssignmentDto> LoadStaffAssignmentDtoAsync(
        IApplicationDbContext context,
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var assignment = await context.BoatStaffAssignments
            .Include(x => x.Boat)
            .Include(x => x.StaffUser)
            .Include(x => x.AssignedByUser)
            .Include(x => x.ReplacedByUser)
            .SingleAsync(x => x.Id == assignmentId, cancellationToken);

        return CharterBookingAssignmentSupport.ToStaffAssignmentDto(assignment);
    }
}
