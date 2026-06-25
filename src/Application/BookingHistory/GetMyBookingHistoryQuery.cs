using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.BookingHistory;

public sealed record BookingHistoryItemDto(
    Guid Id,
    string Type,
    BookingMode BookingMode,
    string Code,
    DateTimeOffset CreatedAt,
    DateOnly? DepartureDate,
    TimeOnly? DepartureTime,
    string? FromStationName,
    string? ToStationName,
    int PassengerCount,
    string Status,
    decimal? TotalAmount,
    string? Currency,
    string DetailEndpoint);

public sealed record GetMyBookingHistoryQuery : IRequest<IReadOnlyList<BookingHistoryItemDto>>;

public sealed class GetMyBookingHistoryQueryHandler
    : IRequestHandler<GetMyBookingHistoryQuery, IReadOnlyList<BookingHistoryItemDto>>
{
    private const string StandardBookingType = "StandardBooking";
    private const string DefaultCurrency = "VND";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetMyBookingHistoryQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BookingHistoryItemDto>> Handle(
        GetMyBookingHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        var standardBookings = await _context.Set<Booking>()
            .AsNoTracking()
            .Include(x => x.Passengers)
            .Include(x => x.Trip)
                .ThenInclude(x => x!.Route)
                    .ThenInclude(x => x.RouteStops)
                        .ThenInclude(x => x.Station)
            .Where(x => EF.Property<string>(x, "booking_type") == "SeatBooking")
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        return standardBookings
            .Select(CreateStandardBookingHistoryItem)
            .OrderByDescending(x => x.CreatedAt)
            .ToArray();
    }

    private static BookingHistoryItemDto CreateStandardBookingHistoryItem(Booking booking)
    {
        var stops = booking.Trip?.Route.RouteStops.OrderBy(x => x.StopOrder).ToArray() ?? [];
        var firstStop = stops.FirstOrDefault();
        var lastStop = stops.LastOrDefault();
        var departure = booking.Trip?.DepartureTime;

        return new BookingHistoryItemDto(
            booking.Id,
            StandardBookingType,
            BookingMode.SeatBased,
            booking.BookingCode,
            booking.Created,
            departure.HasValue ? DateOnly.FromDateTime(departure.Value.LocalDateTime) : null,
            departure.HasValue ? TimeOnly.FromDateTime(departure.Value.LocalDateTime) : null,
            firstStop?.Station.StationName,
            lastStop?.Station.StationName,
            booking.Passengers.Count,
            booking.BookingStatus.ToString(),
            booking.TotalAmount,
            DefaultCurrency,
            $"/api/bookings/{booking.Id}");
    }
}
