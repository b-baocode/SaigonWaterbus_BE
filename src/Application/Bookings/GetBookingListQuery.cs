using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
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
                b.PointsUsed,
                b.PointsEarned,
                b.InsuranceSnapshots,
                ItemCount = b.Passengers.Count,
                RouteType = b.Trip != null ? b.Trip.Route.RouteType : null,
                BoatImageUrl = b.Trip != null && b.Trip.Boat != null ? b.Trip.Boat.ImageUrl : null
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
                BookingInsuranceDtoMapper.ToDto((b.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>()).FirstOrDefault()),
                b.BoatImageUrl))
            .ToList();
    }
}
