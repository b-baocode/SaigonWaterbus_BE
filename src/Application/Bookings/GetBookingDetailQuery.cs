using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

public sealed record GetBookingDetailQuery(Guid BookingId) : IRequest<BookingDetailDto>;

public sealed class GetBookingDetailQueryHandler : IRequestHandler<GetBookingDetailQuery, BookingDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBookingDetailQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<BookingDetailDto> Handle(GetBookingDetailQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        var booking = await _context.Set<Booking>()
            .Include(b => b.Promotion)
            .Include(b => b.Items)
                .ThenInclude(i => i.TicketType)
            .Include(b => b.Items)
                .ThenInclude(i => i.Trip)
            .Include(b => b.Items)
                .ThenInclude(i => i.FromTripStop)
                    .ThenInclude(ts => ts.RouteStop)
                        .ThenInclude(rs => rs.Station)
            .Include(b => b.Items)
                .ThenInclude(i => i.ToTripStop)
                    .ThenInclude(ts => ts.RouteStop)
                        .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        if (booking.UserId != userId)
            throw new NotFoundException("Booking not found.");

        var items = booking.Items.Select(i => new BookingItemDto(
            i.Id,
            i.Trip.TripCode,
            i.PassengerName,
            i.PassengerPhone,
            i.TicketType.TicketTypeName,
            null,
            i.FromTripStop.RouteStop.Station.StationName,
            i.ToTripStop.RouteStop.Station.StationName,
            i.FromTripStop.ScheduledDeparture,
            i.ToTripStop.ScheduledArrival,
            i.UnitPrice,
            i.ItemStatus.ToString())).ToList();

        return new BookingDetailDto(
            booking.Id, booking.BookingCode,
            booking.BookedAt, booking.BookingStatus.ToString(),
            booking.SubtotalAmount, booking.DiscountAmount, booking.TotalAmount,
            booking.PointsUsed, booking.PointsEarned,
            booking.Promotion?.PromotionCode,
            items);
    }
}
