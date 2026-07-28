using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Payments;

/// <summary>
/// Gửi email vé điện tử cho booking thường sau khi thanh toán đủ:
/// - Người đặt vé (ContactEmail) nhận 1 email tổng: QR chung + toàn bộ QR riêng + PDF đính kèm đầy đủ vé.
/// - Hành khách nào có nhập email nhận thêm 1 email chứa vé của riêng người đó + PDF 1 vé
///   (không có QR chung — QR chung chỉ người đặt vé có).
/// Booking khứ hồi: email tổng gộp cả 2 chiều (Legs), email hành khách hiển thị đúng chiều của vé đó.
/// </summary>
internal static class RegularBookingETicketSupport
{
    public static async Task SendETicketEmailsAsync(
        IApplicationDbContext context,
        IPaymentNotificationSender paymentNotificationSender,
        Booking booking,
        Payment payment,
        IReadOnlyList<Ticket> tickets,
        CancellationToken cancellationToken,
        IBookingTicketPdfRenderer? pdfRenderer = null)
    {
        if (tickets.Count == 0 || !payment.PaidAt.HasValue)
        {
            return;
        }

        var passengers = await context.Set<BookingPassenger>()
            .AsNoTracking()
            .Include(p => p.TripSeat)
                .ThenInclude(ts => ts!.Seat)
            .Include(p => p.FromStation)
            .Include(p => p.ToStation)
            .Where(p => p.BookingId == booking.Id)
            .ToListAsync(cancellationToken);

        var outboundLeg = await LoadLegAsync(context, booking.TripId, cancellationToken);
        var returnLeg = booking.ReturnTripId.HasValue
            ? await LoadLegAsync(context, booking.ReturnTripId, cancellationToken)
            : null;

        var ticketsByPassengerId = tickets
            .Where(x => x.BookingPassengerId.HasValue)
            .GroupBy(x => x.BookingPassengerId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IssuedAt).First());
        var companionByPassengerId = LapInfantTicketSupport.AssignCompanionTicketPassengersToAdults(passengers);

        foreach (var passenger in passengers.OrderBy(x => x.TripSeat?.Seat?.Code).ThenBy(x => x.FullName))
        {
            var ticketPassengerId = companionByPassengerId.TryGetValue(passenger.Id, out var companionId)
                ? companionId
                : passenger.Id;
            if (!ticketsByPassengerId.TryGetValue(ticketPassengerId, out var ticket))
            {
                continue;
            }

            // Hành khách thuộc chiều nào thì vé nằm trong leg đó (TripId null = dữ liệu cũ → chiều đi).
            var leg = returnLeg is not null && passenger.TripId == booking.ReturnTripId
                ? returnLeg
                : outboundLeg;

            // Giờ boarding = giờ tàu rời bến LÊN của khách (theo chặng), không phải giờ đầu chuyến.
            DateTimeOffset? boardingTime = leg.Trip is null
                ? null
                : Trips.TripStopScheduleSupport.ResolveSegmentTimes(
                    leg.Trip, passenger.FromStopOrder, passenger.ToStopOrder).Departure;

            TicketTypePricing.TryGet(passenger.PassengerType, out var ticketType);
            var eTicket = new ETicketPassenger(
                passenger.FullName,
                passenger.TripSeat?.Seat?.Code,
                ticketType.Name,
                ticket.TicketCode,
                ticket.QrToken,
                passenger.Email,
                passenger.FromStation?.StationName,
                passenger.ToStation?.StationName,
                boardingTime);

            leg.Tickets.Add(eTicket);
        }

        var allTickets = outboundLeg.Tickets
            .Concat(returnLeg?.Tickets ?? Enumerable.Empty<ETicketPassenger>())
            .ToList();
        if (allTickets.Count == 0)
        {
            return;
        }

        var legs = returnLeg is null ? null : new[] { outboundLeg, returnLeg };
        var contactEmail = booking.ContactEmail?.Trim();

        if (!string.IsNullOrWhiteSpace(contactEmail))
        {
            var bookerNotification = PaymentSupport.CreatePaymentSucceededNotification(
                booking,
                payment,
                contactEmail,
                booking.ContactName);

            // Bản người đặt: PDF có trang QR tổng + toàn bộ vé (khứ hồi: đủ cả 2 chiều).
            var bookerAttachments = RenderPdfAttachment(
                pdfRenderer,
                booking,
                outboundLeg,
                booking.CharterBookingQrToken,
                allTickets,
                legs,
                $"{booking.BookingCode}-tickets.pdf");

            await paymentNotificationSender.SendETicketsAsync(
                new ETicketNotification(
                    bookerNotification,
                    booking.CharterBookingQrToken,
                    outboundLeg.Trip?.TripCode,
                    outboundLeg.Trip?.Route.RouteName,
                    outboundLeg.Trip?.DepartureTime,
                    outboundLeg.Trip?.ArrivalTime,
                    outboundLeg.FromStationName,
                    outboundLeg.ToStationName,
                    allTickets,
                    bookerAttachments,
                    legs?.Select(ToETicketLeg).ToList()),
                cancellationToken);
        }

        foreach (var leg in legs ?? [outboundLeg])
        {
            foreach (var eTicket in leg.Tickets)
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

                // Bản hành khách: chỉ vé của người đó theo đúng chiều, không có QR tổng.
                var passengerAttachments = RenderPdfAttachment(
                    pdfRenderer,
                    booking,
                    leg,
                    bookingQrToken: null,
                    [eTicket],
                    legs: null,
                    $"{eTicket.TicketCode}-boarding-pass.pdf");

                await paymentNotificationSender.SendETicketsAsync(
                    new ETicketNotification(
                        passengerNotification,
                        BookingQrToken: null,
                        leg.Trip?.TripCode,
                        leg.Trip?.Route.RouteName,
                        leg.Trip?.DepartureTime,
                        leg.Trip?.ArrivalTime,
                        leg.FromStationName,
                        leg.ToStationName,
                        [eTicket],
                        passengerAttachments),
                    cancellationToken);
            }
        }
    }

    private sealed record LegContext(
        Trip? Trip,
        string? FromStationName,
        string? ToStationName)
    {
        public List<ETicketPassenger> Tickets { get; } = [];
    }

    private static async Task<LegContext> LoadLegAsync(
        IApplicationDbContext context,
        Guid? tripId,
        CancellationToken cancellationToken)
    {
        Trip? trip = null;
        if (tripId.HasValue)
        {
            trip = await context.Set<Trip>()
                .AsNoTracking()
                .Include(t => t.Boat)
                .Include(t => t.Route)
                    .ThenInclude(r => r.RouteStops)
                        .ThenInclude(rs => rs.Station)
                .Include(t => t.TripStops)
                .SingleOrDefaultAsync(t => t.Id == tripId.Value, cancellationToken);
        }

        var stops = trip?.Route.RouteStops.OrderBy(x => x.StopOrder).ToArray() ?? [];
        return new LegContext(
            trip,
            stops.FirstOrDefault()?.Station.StationName,
            stops.LastOrDefault()?.Station.StationName);
    }

    private static ETicketLeg ToETicketLeg(LegContext leg) =>
        new(leg.Trip?.TripCode,
            leg.Trip?.Route.RouteName,
            leg.Trip?.DepartureTime,
            leg.Trip?.ArrivalTime,
            leg.FromStationName,
            leg.ToStationName,
            leg.Tickets);

    private static BookingTicketPdfLegDto ToPdfLeg(LegContext leg) =>
        new(leg.Trip?.TripCode,
            leg.Trip?.Route.RouteName,
            leg.Trip?.DepartureTime,
            leg.Trip?.ArrivalTime,
            leg.FromStationName,
            leg.ToStationName,
            leg.Trip?.Boat?.Name,
            leg.Tickets.Select(ToPdfItem).ToList());

    private static BookingTicketPdfItemDto ToPdfItem(ETicketPassenger x) =>
        new(x.PassengerName,
            x.SeatCode,
            x.TicketTypeName,
            x.TicketCode,
            x.QrToken,
            x.FromStationName,
            x.ToStationName,
            x.DepartureTime);

    private static IReadOnlyList<EmailAttachment>? RenderPdfAttachment(
        IBookingTicketPdfRenderer? pdfRenderer,
        Booking booking,
        LegContext flatLeg,
        string? bookingQrToken,
        IReadOnlyList<ETicketPassenger> tickets,
        IReadOnlyList<LegContext>? legs,
        string fileName)
    {
        if (pdfRenderer is null)
        {
            return null;
        }

        var export = new BookingTicketPdfExportDto(
            booking.BookingCode,
            flatLeg.Trip?.TripCode,
            flatLeg.Trip?.Route.RouteName,
            flatLeg.Trip?.DepartureTime,
            flatLeg.Trip?.ArrivalTime,
            flatLeg.FromStationName,
            flatLeg.ToStationName,
            flatLeg.Trip?.Boat?.Name,
            bookingQrToken,
            tickets.Select(ToPdfItem).ToList(),
            legs?.Select(ToPdfLeg).ToList());

        return [new EmailAttachment(fileName, "application/pdf", pdfRenderer.Render(export))];
    }
}
