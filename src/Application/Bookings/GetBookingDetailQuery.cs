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
            .Include(b => b.Passengers)
            .Include(b => b.Trip)
                .ThenInclude(t => t!.Route)
                    .ThenInclude(r => r.RouteStops)
                        .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(
                b => b.Id == request.BookingId && EF.Property<string>(b, "booking_type") == "SeatBooking",
                cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        if (booking.UserId != userId)
            throw new NotFoundException("Booking not found.");

        var stops = booking.Trip?.Route.RouteStops.OrderBy(x => x.StopOrder).ToArray() ?? [];
        var fromStop = stops.FirstOrDefault();
        var toStop = stops.LastOrDefault();
        var unitPrice = booking.Passengers.Count == 0 ? 0 : booking.SubtotalAmount / booking.Passengers.Count;

        var items = booking.Passengers.Select(i => new BookingItemDto(
            i.Id,
            booking.Trip?.TripCode ?? string.Empty,
            i.FullName,
            i.PhoneNumber,
            i.PassengerType ?? "Passenger",
            null,
            fromStop?.Station.StationName ?? string.Empty,
            toStop?.Station.StationName ?? string.Empty,
            booking.Trip?.DepartureTime,
            booking.Trip?.ArrivalTime,
            unitPrice,
            booking.BookingStatus.ToString())).ToList();

        return new BookingDetailDto(
            booking.Id, booking.BookingCode,
            booking.Created, booking.BookingStatus.ToString(),
            booking.SubtotalAmount, booking.DiscountAmount, booking.TotalAmount,
            booking.PointsUsed, booking.PointsEarned,
            booking.Promotion?.PromotionCode,
            items);
    }
}
