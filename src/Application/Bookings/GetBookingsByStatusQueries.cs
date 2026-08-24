using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

// ================================ BOOKING STATUS ================================

public sealed record GetBookingsByStatusQuery(BookingStatus Status) : IRequest<IReadOnlyList<BookingListItemDto>>;

public sealed class GetBookingsByStatusQueryHandler
    : IRequestHandler<GetBookingsByStatusQuery, IReadOnlyList<BookingListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBookingsByStatusQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BookingListItemDto>> Handle(
        GetBookingsByStatusQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId ?? throw new ValidationException([]);

        var rows = await _context.Set<Booking>()
            .Where(b => b.BookingType == Booking.SeatBookingType)
            .Where(b => b.UserId == userId)
            .Where(b => b.BookingStatus == request.Status)
            .OrderByDescending(b => b.Created)
            .Select(b => new
            {
                b.Id,
                b.BookingCode,
                b.Created,
                b.BookingStatus,
                b.TotalAmount,
                b.PointsUsed,
                b.PointsEarned,
                b.InsuranceSnapshots,
                ItemCount = b.Passengers.Count,
                RouteType = b.Trip != null ? b.Trip.Route.RouteType : null
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(b => new BookingListItemDto(
                b.Id,
                b.BookingCode,
                b.Created,
                b.BookingStatus.ToString(),
                b.TotalAmount,
                b.ItemCount,
                BookingServiceTypes.Resolve(b.RouteType),
                b.RouteType,
                b.PointsUsed,
                b.PointsEarned,
                BookingInsuranceDtoMapper.ToDto((b.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>()).FirstOrDefault())))
            .ToList();
    }
}

// ================================ PAYMENT STATUS ================================

public sealed record GetBookingsByPaymentStatusQuery(string PaymentStatus) : IRequest<IReadOnlyList<BookingListItemDto>>;

public sealed class GetBookingsByPaymentStatusQueryHandler
    : IRequestHandler<GetBookingsByPaymentStatusQuery, IReadOnlyList<BookingListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBookingsByPaymentStatusQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BookingListItemDto>> Handle(
        GetBookingsByPaymentStatusQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId ?? throw new ValidationException([]);

        var rows = await _context.Set<Booking>()
            .Where(b => b.BookingType == Booking.SeatBookingType)
            .Where(b => b.UserId == userId)
            .Where(b => string.Equals(b.PaymentStatus, request.PaymentStatus.ToString(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.Created)
            .Select(b => new
            {
                b.Id,
                b.BookingCode,
                b.Created,
                b.BookingStatus,
                b.TotalAmount,
                b.PointsUsed,
                b.PointsEarned,
                b.InsuranceSnapshots,
                ItemCount = b.Passengers.Count,
                RouteType = b.Trip != null ? b.Trip.Route.RouteType : null
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(b => new BookingListItemDto(
                b.Id,
                b.BookingCode,
                b.Created,
                b.BookingStatus.ToString(),
                b.TotalAmount,
                b.ItemCount,
                BookingServiceTypes.Resolve(b.RouteType),
                b.RouteType,
                b.PointsUsed,
                b.PointsEarned,
                BookingInsuranceDtoMapper.ToDto((b.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>()).FirstOrDefault())))
            .ToList();
    }
}

// ================================ TICKET (CHECK-IN/CHECK-OUT) STATUS ================================

public sealed record GetBookingsByTicketStatusQuery(TicketStatus TicketStatus) : IRequest<IReadOnlyList<BookingListItemDto>>;

public sealed class GetBookingsByTicketStatusQueryHandler
    : IRequestHandler<GetBookingsByTicketStatusQuery, IReadOnlyList<BookingListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBookingsByTicketStatusQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BookingListItemDto>> Handle(
        GetBookingsByTicketStatusQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId ?? throw new ValidationException([]);

        var rows = await _context.Set<Booking>()
            .Where(b => b.BookingType == Booking.SeatBookingType)
            .Where(b => b.UserId == userId)
            .Where(b => b.Tickets.Any(t => t.TicketStatus == request.TicketStatus))
            .OrderByDescending(b => b.Created)
            .Select(b => new
            {
                b.Id,
                b.BookingCode,
                b.Created,
                b.BookingStatus,
                b.TotalAmount,
                b.PointsUsed,
                b.PointsEarned,
                b.InsuranceSnapshots,
                ItemCount = b.Passengers.Count,
                RouteType = b.Trip != null ? b.Trip.Route.RouteType : null
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(b => new BookingListItemDto(
                b.Id,
                b.BookingCode,
                b.Created,
                b.BookingStatus.ToString(),
                b.TotalAmount,
                b.ItemCount,
                BookingServiceTypes.Resolve(b.RouteType),
                b.RouteType,
                b.PointsUsed,
                b.PointsEarned,
                BookingInsuranceDtoMapper.ToDto((b.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>()).FirstOrDefault())))
            .ToList();
    }
}
