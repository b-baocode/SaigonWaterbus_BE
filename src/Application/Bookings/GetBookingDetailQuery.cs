using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
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
            .AsNoTracking()
            .Include(b => b.Promotion)
            .SingleOrDefaultAsync(
                b => b.Id == request.BookingId && b.BookingType == Booking.SeatBookingType,
                cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        if (booking.UserId != userId)
            throw new NotFoundException("Booking not found.");

        booking.Trip = await LoadTripAsync(booking.TripId, cancellationToken);
        booking.ReturnTrip = await LoadTripAsync(booking.ReturnTripId, cancellationToken);
        booking.Passengers = await LoadPassengersAsync(booking.Id, cancellationToken);
        booking.Tickets = await LoadTicketsAsync(booking.Id, cancellationToken);
        booking.Payments = await LoadPaymentsAsync(booking.Id, cancellationToken);

        var ticketsByPassengerId = booking.Tickets
            .Where(t => t.BookingPassengerId.HasValue
                     && t.TicketStatus != Domain.Enums.TicketStatus.Cancelled
                     && t.TicketStatus != Domain.Enums.TicketStatus.Expired)
            .GroupBy(t => t.BookingPassengerId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.IssuedAt).First());

        var items = booking.Passengers.Select(i =>
        {
            // Booking khứ hồi: mỗi hành khách thuộc một chiều — lấy trip theo passenger.TripId.
            var legTrip = i.TripId.HasValue && i.TripId == booking.ReturnTripId
                ? booking.ReturnTrip
                : booking.Trip;
            var stops = legTrip?.Route.RouteStops.OrderBy(x => x.StopOrder).ToArray() ?? [];
            var fromStop = stops.FirstOrDefault();
            var toStop = stops.LastOrDefault();
            var fromStationId = i.FromStationId ?? fromStop?.StationId;
            var toStationId = i.ToStationId ?? toStop?.StationId;

            // Giờ đi/đến theo CHẶNG của hành khách (trip_stops), không phải đầu/cuối nguyên chuyến.
            var segmentTimes = legTrip is null
                ? default((DateTimeOffset Departure, DateTimeOffset Arrival)?)
                : Trips.TripStopScheduleSupport.ResolveSegmentTimes(legTrip, i.FromStopOrder, i.ToStopOrder);

            ticketsByPassengerId.TryGetValue(i.Id, out var ticket);
            return new BookingItemDto(
                i.Id,
                legTrip?.TripCode ?? string.Empty,
                i.FullName,
                i.PhoneNumber,
                i.PassengerType ?? "Passenger",
                i.TripSeat?.Seat?.Code,
                // Chặng riêng của hành khách (ghế bán theo chặng); dữ liệu cũ chưa lưu trạm
                // thì rơi về trạm đầu/cuối tuyến như trước.
                i.FromStation?.StationName ?? fromStop?.Station.StationName ?? string.Empty,
                i.ToStation?.StationName ?? toStop?.Station.StationName ?? string.Empty,
                segmentTimes?.Departure,
                segmentTimes?.Arrival,
                i.UnitPrice ?? 0,
                booking.BookingStatus.ToString(),
                ticket?.TicketCode,
                ticket?.QrToken,
                ticket?.TicketStatus.ToString(),
                fromStationId,
                toStationId);
        }).ToList();

        return new BookingDetailDto(
            booking.Id, booking.BookingCode,
            booking.Created, booking.BookingStatus.ToString(),
            booking.SubtotalAmount, booking.DiscountAmount, booking.TotalAmount,
            booking.PointsUsed, booking.PointsEarned,
            booking.Promotion?.PromotionCode,
            items,
            booking.PaymentStatus,
            booking.CharterBookingQrToken,
            booking.HoldExpiresAt,
            booking.Payments
                .OrderByDescending(x => x.Created)
                .Select(x => new BookingPaymentDto(
                    x.Id,
                    x.PaymentCode,
                    x.Provider,
                    x.ProviderTransactionId,
                    x.Amount,
                    x.Currency,
                    x.PaymentMethod,
                    x.PaymentPurpose,
                    x.PaymentStatus,
                    x.CheckoutUrl,
                    x.QrCode,
                    x.PaidAt,
                    PaymentSupport.ResolvePaymentExpiresAt(x),
                    x.RefundAmount,
                    x.RefundRequestedAmount,
                    x.RefundMethod,
                    x.RefundReason,
                    x.RefundReferenceId,
                    x.RefundPayoutId,
                    x.RefundStatus,
                    x.RefundFailureReason,
                    x.RefundProcessedByUserId,
                    x.RefundedAt))
                .ToList(),
            BookingServiceTypes.Resolve(booking.Trip?.Route.RouteType),
            booking.Trip?.Route.RouteType,
            booking.ReturnTrip?.TripCode,
            booking.ReturnTrip?.DepartureTime,
            BookingInsuranceDtoMapper.ToDto(booking.InsuranceSnapshot));
    }

    private async Task<Trip?> LoadTripAsync(Guid? tripId, CancellationToken cancellationToken)
    {
        if (!tripId.HasValue)
        {
            return null;
        }

        return await _context.Set<Trip>()
            .AsNoTracking()
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .Include(t => t.TripStops)
            .SingleOrDefaultAsync(t => t.Id == tripId.Value, cancellationToken);
    }

    private async Task<List<BookingPassenger>> LoadPassengersAsync(
        Guid bookingId,
        CancellationToken cancellationToken) =>
        await _context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(p => p.BookingId == bookingId)
            .Include(p => p.TripSeat)
                .ThenInclude(ts => ts!.Seat)
            .Include(p => p.FromStation)
            .Include(p => p.ToStation)
            .ToListAsync(cancellationToken);

    private async Task<List<Ticket>> LoadTicketsAsync(
        Guid bookingId,
        CancellationToken cancellationToken) =>
        await _context.Set<Ticket>()
            .AsNoTracking()
            .Where(t => t.BookingId == bookingId)
            .ToListAsync(cancellationToken);

    private async Task<List<Payment>> LoadPaymentsAsync(
        Guid bookingId,
        CancellationToken cancellationToken) =>
        await _context.Set<Payment>()
            .AsNoTracking()
            .Where(p => p.BookingId == bookingId)
            .ToListAsync(cancellationToken);
}
