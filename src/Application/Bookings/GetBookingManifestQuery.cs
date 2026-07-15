using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Bookings;

public sealed record BookingManifestPassengerDto(
    Guid PassengerId,
    string FullName,
    string? PassengerType,
    string? SeatNumber,
    Guid? TicketId,
    string? TicketCode,
    string? QrToken,
    string? TicketStatus,
    DateTimeOffset? CheckedInAt,
    string? CheckedInByName,
    bool CanCheckIn,
    string? TripCode = null,
    string? FromStationName = null,
    string? ToStationName = null);

// Booking khứ hồi: các field Return* mô tả chiều về (null với booking một chiều);
// mỗi passenger mang TripCode của chiều mình thuộc về.
public sealed record BookingManifestDto(
    Guid BookingId,
    string BookingCode,
    string? BookingQrToken,
    string BookingStatus,
    string PaymentStatus,
    string ContactName,
    string ContactPhone,
    string? ContactEmail,
    string? TripCode,
    string? RouteName,
    DateTimeOffset? DepartureTime,
    DateTimeOffset? ArrivalTime,
    string? FromStationName,
    string? ToStationName,
    int PassengerCount,
    int ActiveTicketCount,
    int CheckedInTicketCount,
    IReadOnlyList<BookingManifestPassengerDto> Passengers,
    string? ReturnTripCode = null,
    string? ReturnRouteName = null,
    DateTimeOffset? ReturnDepartureTime = null,
    DateTimeOffset? ReturnArrivalTime = null,
    string? ReturnFromStationName = null,
    string? ReturnToStationName = null);

public sealed record GetBookingManifestByCodeQuery(string BookingCode) : IRequest<BookingManifestDto>;

public sealed record GetBookingManifestByQrTokenQuery(string QrToken) : IRequest<BookingManifestDto>;

public sealed class GetBookingManifestByCodeQueryValidator : AbstractValidator<GetBookingManifestByCodeQuery>
{
    public GetBookingManifestByCodeQueryValidator()
    {
        RuleFor(x => x.BookingCode).NotEmpty().MaximumLength(50);
    }
}

public sealed class GetBookingManifestByQrTokenQueryValidator : AbstractValidator<GetBookingManifestByQrTokenQuery>
{
    public GetBookingManifestByQrTokenQueryValidator()
    {
        RuleFor(x => x.QrToken).NotEmpty().MaximumLength(100);
    }
}

public sealed class GetBookingManifestQueryHandler :
    IRequestHandler<GetBookingManifestByCodeQuery, BookingManifestDto>,
    IRequestHandler<GetBookingManifestByQrTokenQuery, BookingManifestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBookingManifestQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<BookingManifestDto> Handle(
        GetBookingManifestByCodeQuery request,
        CancellationToken cancellationToken)
    {
        var normalizedBookingCode = request.BookingCode.Trim().ToUpperInvariant();
        return await GetManifestAsync(
            x => x.BookingCode.ToUpper() == normalizedBookingCode,
            cancellationToken);
    }

    public async Task<BookingManifestDto> Handle(
        GetBookingManifestByQrTokenQuery request,
        CancellationToken cancellationToken)
    {
        var qrToken = request.QrToken.Trim();
        return await GetManifestAsync(
            x => x.CharterBookingQrToken == qrToken,
            cancellationToken);
    }

    private async Task<BookingManifestDto> GetManifestAsync(
        System.Linq.Expressions.Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);

        var booking = await BookingManifestSupport.BuildQuery(_context)
            .Where(x => x.BookingType == Booking.SeatBookingType)
            .SingleOrDefaultAsync(predicate, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        BookingManifestSupport.EnsureCanView(currentUser, booking);
        return BookingManifestSupport.ToDto(booking);
    }
}

internal static class BookingManifestSupport
{
    public static IQueryable<Booking> BuildQuery(IApplicationDbContext context) =>
        context.Set<Booking>()
            .AsNoTracking()
            .Include(x => x.Trip)
                .ThenInclude(t => t!.Route)
                    .ThenInclude(r => r.RouteStops)
                        .ThenInclude(rs => rs.Station)
            .Include(x => x.ReturnTrip)
                .ThenInclude(t => t!.Route)
                    .ThenInclude(r => r.RouteStops)
                        .ThenInclude(rs => rs.Station)
            .Include(x => x.Passengers)
                .ThenInclude(p => p.TripSeat)
                    .ThenInclude(ts => ts!.Seat)
            .Include(x => x.Passengers)
                .ThenInclude(p => p.FromStation)
            .Include(x => x.Passengers)
                .ThenInclude(p => p.ToStation)
            .Include(x => x.Tickets)
                .ThenInclude(t => t.CheckedInByUser);

    public static void EnsureCanView(User currentUser, Booking booking)
    {
        if (booking.UserId != currentUser.Id
            && !AuthSupport.IsAdmin(currentUser)
            && !AuthSupport.IsManager(currentUser)
            && !AuthSupport.IsStaff(currentUser))
        {
            throw new NotFoundException("Booking not found.");
        }
    }

    public static bool CanCheckInBooking(Booking booking) =>
        booking.BookingStatus == BookingStatus.Confirmed
        && (string.Equals(booking.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)
            || booking.RemainingAmount <= 0);

    public static BookingManifestDto ToDto(Booking booking)
    {
        var currentTickets = booking.Tickets
            .Where(x => x.TicketStatus != TicketStatus.Cancelled && x.TicketStatus != TicketStatus.Expired)
            .ToList();
        var ticketsByPassengerId = currentTickets
            .Where(x => x.BookingPassengerId.HasValue)
            .GroupBy(x => x.BookingPassengerId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IssuedAt).First());
        var canCheckInBooking = CanCheckInBooking(booking);

        var stops = booking.Trip?.Route.RouteStops.OrderBy(x => x.StopOrder).ToArray() ?? [];
        var fromStop = stops.FirstOrDefault();
        var toStop = stops.LastOrDefault();

        var returnStops = booking.ReturnTrip?.Route.RouteStops.OrderBy(x => x.StopOrder).ToArray() ?? [];
        var returnFromStop = returnStops.FirstOrDefault();
        var returnToStop = returnStops.LastOrDefault();

        // Booking khứ hồi: sắp xếp theo chiều (đi trước, về sau) rồi mới tới ghế.
        var passengers = booking.Passengers
            .OrderBy(x => x.TripId.HasValue && x.TripId == booking.ReturnTripId ? 1 : 0)
            .ThenBy(x => x.TripSeat?.Seat?.Code)
            .ThenBy(x => x.FullName)
            .Select(passenger =>
            {
                ticketsByPassengerId.TryGetValue(passenger.Id, out var ticket);
                var legTripCode = passenger.TripId.HasValue && passenger.TripId == booking.ReturnTripId
                    ? booking.ReturnTrip?.TripCode
                    : booking.Trip?.TripCode;
                return new BookingManifestPassengerDto(
                    passenger.Id,
                    passenger.FullName,
                    passenger.PassengerType,
                    passenger.TripSeat?.Seat?.Code,
                    ticket?.Id,
                    ticket?.TicketCode,
                    ticket?.QrToken,
                    ticket?.TicketStatus.ToString(),
                    ticket?.CheckedInAt,
                    ticket?.CheckedInByUser?.FullName,
                    canCheckInBooking && ticket?.TicketStatus == TicketStatus.Active,
                    legTripCode,
                    passenger.FromStation?.StationName,
                    passenger.ToStation?.StationName);
            })
            .ToList();

        return new BookingManifestDto(
            booking.Id,
            booking.BookingCode,
            booking.CharterBookingQrToken,
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.ContactName,
            booking.ContactPhone,
            booking.ContactEmail,
            booking.Trip?.TripCode,
            booking.Trip?.Route.RouteName,
            booking.Trip?.DepartureTime,
            booking.Trip?.ArrivalTime,
            fromStop?.Station.StationName,
            toStop?.Station.StationName,
            booking.Passengers.Count,
            currentTickets.Count(x => x.TicketStatus == TicketStatus.Active),
            currentTickets.Count(x => x.TicketStatus == TicketStatus.CheckedIn),
            passengers,
            booking.ReturnTrip?.TripCode,
            booking.ReturnTrip?.Route.RouteName,
            booking.ReturnTrip?.DepartureTime,
            booking.ReturnTrip?.ArrivalTime,
            returnFromStop?.Station.StationName,
            returnToStop?.Station.StationName);
    }
}
