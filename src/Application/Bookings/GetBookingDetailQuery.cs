using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

public sealed record GetBookingDetailQuery(Guid BookingId) : IRequest<BookingDetailDto>;

public sealed class GetBookingDetailQueryHandler : IRequestHandler<GetBookingDetailQuery, BookingDetailDto>
{
    private const string UsedTicketStatus = "Used";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public GetBookingDetailQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        // Vé Cancelled bị loại vì LUÔN có tấm khác thay thế (cấp lại vé, đổi loại vé) — giữ nó
        // lại thì hành khách hiện nhầm tấm cũ.
        //
        // Vé Expired thì NGƯỢC LẠI: không có tấm nào thay thế cả. Trước đây nó cũng bị loại, nên
        // hành khách trắng vé, ticketStatus về null, và màn hình rơi về itemStatus của booking —
        // khách thấy "ĐÃ XÁC NHẬN" kèm câu "vé sẽ hiện sau khi thanh toán" trong khi vé đã chết.
        // Giữ lại để trả đúng trạng thái; FE tự ẩn QR vì Expired không nằm trong nhóm còn hiệu lực.
        var ticketsByPassengerId = booking.Tickets
            .Where(t => t.BookingPassengerId.HasValue
                     && t.TicketStatus != Domain.Enums.TicketStatus.Cancelled)
            .GroupBy(t => t.BookingPassengerId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g
                    // Vé còn dùng được luôn được ưu tiên; chỉ khi không còn tấm nào mới lấy vé
                    // đã hết hạn để báo trạng thái thật.
                    .OrderByDescending(t => t.TicketStatus != Domain.Enums.TicketStatus.Expired)
                    .ThenByDescending(t => t.IssuedAt)
                    .First());
        var passengerById = booking.Passengers.ToDictionary(x => x.Id);
        var companionByPassengerId = LapInfantTicketSupport.AssignCompanionTicketPassengersToAdults(booking.Passengers);
        var now = _timeProvider.GetUtcNow();

        var items = booking.Passengers.Select(i =>
        {
            // Booking khứ hồi: mỗi hành khách thuộc một chiều — lấy trip theo passenger.TripId.
            var legTrip = i.TripId.HasValue && i.TripId == booking.ReturnTripId
                ? booking.ReturnTrip
                : booking.Trip;
            var stops = (legTrip?.Route?.RouteStops ?? Enumerable.Empty<RouteStop>())
                .OrderBy(x => x.StopOrder)
                .ToArray();
            var fromStop = stops.FirstOrDefault();
            var toStop = stops.LastOrDefault();
            var fromStationId = i.FromStationId ?? fromStop?.StationId;
            var toStationId = i.ToStationId ?? toStop?.StationId;

            // Giờ đi/đến theo CHẶNG của hành khách (trip_stops), không phải đầu/cuối nguyên chuyến.
            var segmentTimes = legTrip is null
                ? default((DateTimeOffset Departure, DateTimeOffset Arrival)?)
                : Trips.TripStopScheduleSupport.ResolveSegmentTimes(legTrip, i.FromStopOrder, i.ToStopOrder);

            var isLapInfant = LapInfantTicketSupport.IsLapInfant(i);
            var usesCompanionTicket = LapInfantTicketSupport.UsesCompanionTicket(i);
            BookingPassenger? companion = null;
            if (usesCompanionTicket
                && companionByPassengerId.TryGetValue(i.Id, out var companionPassengerId)
                && passengerById.TryGetValue(companionPassengerId, out var assignedCompanion))
            {
                companion = assignedCompanion;
            }

            var ticketPassengerId = companion?.Id ?? i.Id;
            ticketsByPassengerId.TryGetValue(ticketPassengerId, out var ticket);
            return new BookingItemDto(
                i.Id,
                legTrip?.TripCode ?? string.Empty,
                i.FullName,
                i.PhoneNumber,
                i.PassengerType ?? "Passenger",
                i.TripSeat?.Seat?.Code,
                // Chặng riêng của hành khách (ghế bán theo chặng); dữ liệu cũ chưa lưu trạm
                // thì rơi về trạm đầu/cuối tuyến như trước.
                i.FromStation?.StationName ?? fromStop?.Station?.StationName ?? string.Empty,
                i.ToStation?.StationName ?? toStop?.Station?.StationName ?? string.Empty,
                segmentTimes?.Departure,
                segmentTimes?.Arrival,
                i.UnitPrice ?? 0,
                booking.BookingStatus.ToString(),
                ticket?.TicketCode,
                ticket?.QrToken,
                ResolveDisplayTicketStatus(ticket, legTrip, now),
                fromStationId,
                toStationId,
                isLapInfant,
                companion?.Id,
                companion?.FullName,
                usesCompanionTicket && companion is not null,
                i.BirthYear,
                legTrip?.Boat?.Code,
                legTrip?.Boat?.Name,
                legTrip?.Id,
                i.Email,
                i.TripSeat?.Seat?.SeatTypeCode,
                i.TripSeat?.Seat?.SeatType?.Name);
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
            BookingServiceTypes.Resolve(booking.Trip?.Route?.RouteType),
            booking.Trip?.Route?.RouteType,
            booking.ReturnTrip?.TripCode,
            booking.ReturnTrip?.DepartureTime,
            BookingInsuranceDtoMapper.ToDto(booking.InsuranceSnapshot),
            booking.Trip?.Id,
            booking.Trip?.Boat?.Code,
            booking.Trip?.Boat?.Name,
            booking.ReturnTrip?.Id,
            booking.ReturnTrip?.Boat?.Code,
            booking.ReturnTrip?.Boat?.Name,
            booking.ContactName,
            booking.ContactPhone,
            booking.ContactEmail);
    }

    private static string? ResolveDisplayTicketStatus(
        Ticket? ticket,
        Trip? trip,
        DateTimeOffset now)
    {
        if (ticket is null)
        {
            return null;
        }

        if (ticket.TicketStatus == TicketStatus.Active && IsTripEnded(trip, now))
        {
            return UsedTicketStatus;
        }

        return ticket.TicketStatus.ToString();
    }

    private static bool IsTripEnded(Trip? trip, DateTimeOffset now) =>
        trip is not null
        && (trip.TripStatus == TripStatus.Completed
            || (trip.AdjustedArrivalTime ?? trip.ArrivalTime) <= now);

    private async Task<Trip?> LoadTripAsync(Guid? tripId, CancellationToken cancellationToken)
    {
        if (!tripId.HasValue)
        {
            return null;
        }

        return await _context.Set<Trip>()
            .AsNoTracking()
            .Include(t => t.Boat)
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
                    .ThenInclude(seat => seat!.SeatType)
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
