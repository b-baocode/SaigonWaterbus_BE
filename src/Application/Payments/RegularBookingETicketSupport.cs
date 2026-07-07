using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Payments;

/// <summary>
/// Gửi email vé điện tử cho booking thường sau khi thanh toán đủ:
/// - Người đặt vé (ContactEmail) nhận 1 email tổng: QR chung + toàn bộ QR riêng.
/// - Hành khách nào có nhập email nhận thêm 1 email boarding pass chứa QR riêng của người đó.
/// </summary>
internal static class RegularBookingETicketSupport
{
    public static async Task SendETicketEmailsAsync(
        IApplicationDbContext context,
        IPaymentNotificationSender paymentNotificationSender,
        Booking booking,
        Payment payment,
        IReadOnlyList<Ticket> tickets,
        CancellationToken cancellationToken)
    {
        if (tickets.Count == 0 || !payment.PaidAt.HasValue)
        {
            return;
        }

        var passengers = await context.Set<BookingPassenger>()
            .AsNoTracking()
            .Include(p => p.TripSeat)
                .ThenInclude(ts => ts!.Seat)
            .Where(p => p.BookingId == booking.Id)
            .ToListAsync(cancellationToken);

        Trip? trip = null;
        if (booking.TripId.HasValue)
        {
            trip = await context.Set<Trip>()
                .AsNoTracking()
                .Include(t => t.Route)
                    .ThenInclude(r => r.RouteStops)
                        .ThenInclude(rs => rs.Station)
                .SingleOrDefaultAsync(t => t.Id == booking.TripId.Value, cancellationToken);
        }

        var stops = trip?.Route.RouteStops.OrderBy(x => x.StopOrder).ToArray() ?? [];
        var fromStationName = stops.FirstOrDefault()?.Station.StationName;
        var toStationName = stops.LastOrDefault()?.Station.StationName;

        var ticketsByPassengerId = tickets
            .Where(x => x.BookingPassengerId.HasValue)
            .GroupBy(x => x.BookingPassengerId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IssuedAt).First());

        var eTicketPassengers = new List<ETicketPassenger>();
        foreach (var passenger in passengers.OrderBy(x => x.TripSeat?.Seat?.Code).ThenBy(x => x.FullName))
        {
            if (!ticketsByPassengerId.TryGetValue(passenger.Id, out var ticket))
            {
                continue;
            }

            TicketTypePricing.TryGet(passenger.PassengerType, out var ticketType);
            eTicketPassengers.Add(new ETicketPassenger(
                passenger.FullName,
                passenger.TripSeat?.Seat?.Code,
                ticketType.Name,
                ticket.TicketCode,
                ticket.QrToken,
                passenger.Email));
        }

        if (eTicketPassengers.Count == 0)
        {
            return;
        }

        var contactEmail = booking.ContactEmail?.Trim();

        if (!string.IsNullOrWhiteSpace(contactEmail))
        {
            var bookerNotification = PaymentSupport.CreatePaymentSucceededNotification(
                booking,
                payment,
                contactEmail,
                booking.ContactName);

            await paymentNotificationSender.SendETicketsAsync(
                new ETicketNotification(
                    bookerNotification,
                    booking.CharterBookingQrToken,
                    trip?.TripCode,
                    trip?.Route.RouteName,
                    trip?.DepartureTime,
                    trip?.ArrivalTime,
                    fromStationName,
                    toStationName,
                    eTicketPassengers),
                cancellationToken);
        }

        foreach (var eTicket in eTicketPassengers)
        {
            var passengerEmail = eTicket.Email?.Trim();
            if (string.IsNullOrWhiteSpace(passengerEmail)
                || string.Equals(passengerEmail, contactEmail, StringComparison.OrdinalIgnoreCase))
            {
                // Không có email riêng (hoặc trùng email người đặt) → vé đã nằm trong email tổng.
                continue;
            }

            var passengerNotification = PaymentSupport.CreatePaymentSucceededNotification(
                booking,
                payment,
                passengerEmail,
                eTicket.PassengerName);

            await paymentNotificationSender.SendBoardingPassAsync(
                new BoardingPassNotification(
                    passengerNotification,
                    eTicket.TicketCode,
                    eTicket.QrToken),
                cancellationToken);
        }
    }
}
