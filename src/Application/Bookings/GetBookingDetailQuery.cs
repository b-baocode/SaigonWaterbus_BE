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
            .Include(b => b.Promotion)
            .Include(b => b.Passengers)
                .ThenInclude(p => p.TripSeat)
                    .ThenInclude(ts => ts!.Seat)
            .Include(b => b.Passengers)
                .ThenInclude(p => p.FromStation)
            .Include(b => b.Passengers)
                .ThenInclude(p => p.ToStation)
            .Include(b => b.Trip)
                .ThenInclude(t => t!.Route)
                    .ThenInclude(r => r.RouteStops)
                        .ThenInclude(rs => rs.Station)
            .Include(b => b.ReturnTrip)
                .ThenInclude(t => t!.Route)
                    .ThenInclude(r => r.RouteStops)
                        .ThenInclude(rs => rs.Station)
            .Include(b => b.Tickets)
            .Include(b => b.Payments)
            .SingleOrDefaultAsync(
                b => b.Id == request.BookingId && b.BookingType == Booking.SeatBookingType,
                cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        if (booking.UserId != userId)
            throw new NotFoundException("Booking not found.");

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
                legTrip?.DepartureTime,
                legTrip?.ArrivalTime,
                i.UnitPrice ?? 0,
                booking.BookingStatus.ToString(),
                ticket?.TicketCode,
                ticket?.QrToken,
                ticket?.TicketStatus.ToString());
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
            booking.ReturnTrip?.TripCode,
            booking.ReturnTrip?.DepartureTime);
    }
}
