using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.CustomBookingRequests;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.BookingHistory;

public sealed record BookingHistoryTicketDto(
    Guid Id,
    string TicketCode,
    CustomBookingTicketStatus Status,
    string? QrPayload,
    DateTimeOffset QrIssuedAt,
    DateTimeOffset? QrExpiresAt,
    DateTimeOffset? QrUsedAt);

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
    string DetailEndpoint,
    BookingHistoryTicketDto? Ticket);

public sealed record GetMyBookingHistoryQuery : IRequest<IReadOnlyList<BookingHistoryItemDto>>;

public sealed class GetMyBookingHistoryQueryHandler
    : IRequestHandler<GetMyBookingHistoryQuery, IReadOnlyList<BookingHistoryItemDto>>
{
    private const string StandardBookingType = "StandardBooking";
    private const string CustomBookingType = "CustomBooking";
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
            .Include(x => x.Items)
                .ThenInclude(x => x.Trip)
            .Include(x => x.Items)
                .ThenInclude(x => x.FromTripStop)
                    .ThenInclude(x => x.RouteStop)
                        .ThenInclude(x => x.Station)
            .Include(x => x.Items)
                .ThenInclude(x => x.ToTripStop)
                    .ThenInclude(x => x.RouteStop)
                        .ThenInclude(x => x.Station)
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var customBookings = await _context.Set<CustomBookingRequest>()
            .AsNoTracking()
            .Include(x => x.WaterbusService)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.Quote)
            .Include(x => x.Tickets)
            .Where(x => x.UserId == userId || x.ContactUserId == userId)
            .ToListAsync(cancellationToken);

        return standardBookings
            .Select(CreateStandardBookingHistoryItem)
            .Concat(customBookings.Select(CreateCustomBookingHistoryItem))
            .OrderByDescending(x => x.CreatedAt)
            .ToArray();
    }

    private static BookingHistoryItemDto CreateStandardBookingHistoryItem(Booking booking)
    {
        var firstItem = booking.Items
            .Where(x => x.ItemStatus != BookingItemStatus.Cancelled)
            .OrderBy(x => x.FromTripStop.ScheduledDeparture)
            .FirstOrDefault();

        var departure = firstItem?.FromTripStop.ScheduledDeparture;
        return new BookingHistoryItemDto(
            booking.Id,
            StandardBookingType,
            BookingMode.SeatBased,
            booking.BookingCode,
            booking.BookedAt,
            departure.HasValue ? DateOnly.FromDateTime(departure.Value.LocalDateTime) : null,
            departure.HasValue ? TimeOnly.FromDateTime(departure.Value.LocalDateTime) : null,
            firstItem?.FromTripStop.RouteStop.Station.StationName,
            firstItem?.ToTripStop.RouteStop.Station.StationName,
            booking.Items.Count(x => x.ItemStatus != BookingItemStatus.Cancelled),
            booking.BookingStatus.ToString(),
            booking.TotalAmount,
            DefaultCurrency,
            $"/api/bookings/{booking.Id}",
            null);
    }

    private static BookingHistoryItemDto CreateCustomBookingHistoryItem(CustomBookingRequest booking)
    {
        var ticket = booking.Tickets
            .OrderByDescending(x => x.QrIssuedAt)
            .FirstOrDefault(x => x.Status == CustomBookingTicketStatus.Active)
            ?? booking.Tickets
                .OrderByDescending(x => x.QrIssuedAt)
                .FirstOrDefault();
        var canExposeQr = CustomBookingPaymentSupport.IsFullyPaid(booking.Quote);

        return new BookingHistoryItemDto(
            booking.Id,
            CustomBookingType,
            booking.WaterbusService?.BookingMode ?? BookingMode.VesselRental,
            booking.WaterbusService?.Code ?? "CUSTOM",
            booking.Created,
            booking.DepartureDate,
            booking.PreferredStartTime,
            booking.FromStation?.StationName ?? booking.FromLocation,
            booking.ToStation?.StationName ?? booking.ToLocation,
            booking.PassengerCount,
            booking.Status.ToString(),
            booking.Quote?.QuotedPrice,
            booking.Quote?.Currency,
            $"/api/custom-booking-requests/{booking.Id}",
            ticket is null
                ? null
                : new BookingHistoryTicketDto(
                    ticket.Id,
                    ticket.TicketCode,
                    ticket.Status,
                    !canExposeQr || string.IsNullOrWhiteSpace(ticket.QrToken)
                        ? null
                        : CustomBookingTicketSupport.CreateQrPayload(ticket.QrToken),
                    ticket.QrIssuedAt,
                    ticket.QrExpiresAt,
                    ticket.QrUsedAt));
    }
}
