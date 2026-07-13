using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

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
                && _context.StaffWorkAssignments.Any(a =>
                        a.StaffUserId == actor.Id
                        && a.Status != StaffWorkAssignmentStatus.Cancelled
                        && a.AssignmentType == StaffWorkAssignmentType.Boat
                        && a.BoatId.HasValue
                        && a.WorkingDate == x.DepartureDate.Value
                        && ((x.BoatId.HasValue && a.BoatId == x.BoatId)
                            || x.CharterBoats.Any(cb => a.BoatId == cb.BoatId))));
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
