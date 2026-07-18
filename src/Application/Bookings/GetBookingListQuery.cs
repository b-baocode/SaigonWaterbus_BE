using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

public sealed record GetBookingListQuery : IRequest<IReadOnlyList<BookingListItemDto>>;

public sealed class GetBookingListQueryHandler : IRequestHandler<GetBookingListQuery, IReadOnlyList<BookingListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBookingListQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BookingListItemDto>> Handle(
        GetBookingListQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        // RouteType lấy ở DB, ServiceType map trong bộ nhớ (BookingServiceTypes.Resolve không dịch được sang SQL).
        var rows = await _context.Set<Booking>()
            .Where(b => b.BookingType == Booking.SeatBookingType)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.Created)
            .Select(b => new
            {
                b.Id,
                b.BookingCode,
                b.Created,
                b.BookingStatus,
                b.TotalAmount,
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
                b.RouteType))
            .ToList();
    }
}
