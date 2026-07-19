using FluentValidation.Results;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

/// <summary>Toàn bộ validate + tính giá của một chiều đã resolve xong (trip, loại vé, ghế, chặng, giá).</summary>
internal sealed record ResolvedLeg(
    Trip Trip,
    List<(ResolvedItem Resolved, decimal UnitPrice)> ItemPrices);

internal sealed record ResolvedItem(
    BookingItemRequest Item,
    TicketTypeInfo TicketType,
    Seat? Seat,
    TripSeat? TripSeat,
    RouteStop FromStop,
    RouteStop ToStop);

/// <summary>
/// Resolve một chiều của booking thường: kiểm tra chuyến, loại vé, ghế (trùng chặng, đã bán,
/// đang pre-hold) và tính giá. Dùng chung cho khách tự đặt online (<see cref="CreateBookingCommand"/>)
/// và cho staff bán vé tại quầy (<see cref="CreateCounterBookingCommand"/>).
/// </summary>
internal sealed class BookingLegResolver
{
    private readonly IApplicationDbContext _context;
    private readonly IFareCalculator _fareCalculator;
    private readonly ISeatHoldService _seatHoldService;

    public BookingLegResolver(
        IApplicationDbContext context,
        IFareCalculator fareCalculator,
        ISeatHoldService seatHoldService)
    {
        _context = context;
        _fareCalculator = fareCalculator;
        _seatHoldService = seatHoldService;
    }

    /// <param name="allowDepartedTrip">
    /// true = bán vé tại quầy: bỏ hạn đóng bán trước giờ chạy và cho bán cả khi tàu đang chạy
    /// (Boarding/InProgress/Delayed). false = khách tự đặt: chỉ chuyến Scheduled và còn trước hạn đóng bán.
    /// </param>
    public async Task<ResolvedLeg> ResolveAsync(
        string rawTripCode,
        IReadOnlyList<BookingItemRequest> items,
        Guid holderId,
        DateTimeOffset now,
        string tripCodePropertyName,
        bool allowDepartedTrip,
        CancellationToken cancellationToken)
    {
        var tripCode = rawTripCode.Trim().ToUpperInvariant();

        var trip = await _context.Set<Trip>()
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(t => t.TripCode == tripCode, cancellationToken)
            ?? throw new NotFoundException($"Trip '{tripCode}' not found.");

        if (trip.TripType != TripTypes.Regular)
            throw new ValidationException([new ValidationFailure(tripCodePropertyName,
                "Trip charter không bán vé lẻ.")]);

        EnsureTripSellable(trip, now, tripCodePropertyName, allowDepartedTrip);

        if (!trip.BoatId.HasValue)
            throw new ValidationException([new ValidationFailure(tripCodePropertyName,
                "Trip has no boat assigned.")]);

        var requestedTicketCodes = items
            .Select(i => i.TicketTypeCode.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var ticketTypesByCode = new Dictionary<string, TicketTypeInfo>();
        foreach (var code in requestedTicketCodes)
        {
            if (TicketTypePricing.TryGet(code, out var ticketType))
            {
                ticketTypesByCode[code] = ticketType;
            }
        }

        var missingTicket = requestedTicketCodes.FirstOrDefault(c => !ticketTypesByCode.ContainsKey(c));
        if (missingTicket is not null)
            throw new NotFoundException($"Ticket type '{missingTicket}' not found.");

        // Vé INFANT chỉ dành cho trẻ dưới 2 tuổi — kiểm tra năm sinh so với ngày khởi hành chuyến.
        var departureYear = trip.DepartureTime.Year;
        foreach (var infantItem in items.Where(i => IsInfant(i.TicketTypeCode)))
        {
            if (!infantItem.BirthYear.HasValue)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.BirthYear),
                    "Vé INFANT yêu cầu khai báo birthYear (trẻ dưới 2 tuổi).")]);

            var ageAtDeparture = departureYear - infantItem.BirthYear.Value;
            if (ageAtDeparture < 0 || ageAtDeparture > 2)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.BirthYear),
                    "Vé INFANT chỉ áp dụng cho trẻ dưới 2 tuổi (sinh trong vòng 2 năm so với ngày khởi hành).")]);
        }

        // Trẻ dưới 2 tuổi (INFANT) được phép không chiếm ghế (ngồi cùng người lớn) và MIỄN PHÍ trên
        // cả waterbus thường lẫn sightseeing. Vé free có ghế (SENIOR/DISABLED, hoặc INFANT chọn ghế riêng)
        // vẫn bị chặn tự nhiên trên sightseeing vì ghế sightseeing không phải loại STANDARD — FareCalculator
        // từ chối theo AllowedSeatTypeCodes, nên không cần chặn thêm ở cấp tàu tại đây.
        var lapItems = items.Where(i => string.IsNullOrWhiteSpace(i.SeatNumber)).ToList();
        if (lapItems.Count > 0)
        {
            var nonInfantLapItem = lapItems.FirstOrDefault(i => !IsInfant(i.TicketTypeCode));
            if (nonInfantLapItem is not null)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                    "Chỉ vé INFANT (trẻ dưới 2 tuổi) mới được bỏ trống seatNumber.")]);

            // Mỗi trẻ ngồi lòng (INFANT không ghế) phải có một hành khách có ghế (không phải INFANT) đi kèm
            // trong cùng chiều — 1 người lớn kèm tối đa 1 trẻ. (Cũng khai báo ở validator, nhưng validator
            // không chạy trong pipeline MediatR nên bắt buộc phải enforce tại handler.)
            var seatedCompanionCount = items.Count(i =>
                !string.IsNullOrWhiteSpace(i.SeatNumber) && !IsInfant(i.TicketTypeCode));
            if (lapItems.Count > seatedCompanionCount)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                    "Mỗi trẻ dưới 2 tuổi (INFANT không chiếm ghế) phải có một hành khách người lớn có ghế "
                    + "đi kèm trong cùng booking.")]);
        }

        var usesSegments = DistanceFareSupport.UsesDistanceFare(trip);

        // Trạm lên/xuống resolve trước để biết chặng của từng item — trip Regular bán ghế
        // theo chặng nên mọi check ghế (trùng ghế, occupancy, pre-hold) và giá đều theo chặng.
        var itemSegments = new List<(BookingItemRequest Item, RouteStop FromStop, RouteStop ToStop)>();
        if (usesSegments)
        {
            foreach (var item in items)
            {
                // Bắt buộc ở đây thay vì validator: chỉ chuyến bán theo chặng mới cần trạm lên/xuống.
                if (string.IsNullOrWhiteSpace(item.FromStationCode)
                    || string.IsNullOrWhiteSpace(item.ToStationCode))
                    throw new ValidationException([new ValidationFailure(
                        string.IsNullOrWhiteSpace(item.FromStationCode)
                            ? nameof(BookingItemRequest.FromStationCode)
                            : nameof(BookingItemRequest.ToStationCode),
                        "fromStationCode và toStationCode là bắt buộc trên chuyến bán vé theo chặng.")]);

                // Dùng chung resolver với giữ ghế / sơ đồ ghế — tuyến có thể ghé cùng một bến nhiều
                // lần (vòng lặp Regular) nên không index route stop theo station code được.
                var (fromStop, toStop) = Trips.TripSegmentSupport.ResolveStops(
                    trip.Route.RouteStops,
                    item.FromStationCode,
                    item.ToStationCode,
                    nameof(BookingItemRequest.FromStationCode),
                    nameof(BookingItemRequest.ToStationCode));

                itemSegments.Add((item, fromStop, toStop));
            }
        }
        else
        {
            // Chuyến đi nguyên chuyến (ngắm cảnh): bỏ qua trạm client gửi, dùng bến đầu → bến cuối
            // của tuyến. Tuyến ngắm cảnh chỉ có đúng hai bến và cả hai là một bến, nên KHÔNG được
            // index route stop theo station code — cùng một station nằm ở hai stop order.
            var orderedStops = trip.Route.RouteStops.OrderBy(rs => rs.StopOrder).ToList();
            if (orderedStops.Count < 2)
                throw new ValidationException([new ValidationFailure(tripCodePropertyName,
                    "Tuyến của chuyến này chưa có đủ bến để bán vé.")]);

            var firstStop = orderedStops[0];
            var lastStop = orderedStops[^1];
            foreach (var item in items)
            {
                itemSegments.Add((item, firstStop, lastStop));
            }
        }

        // Trùng ghế trong cùng chiều: trip Regular cho phép hai vé cùng ghế nếu chặng không
        // giao nhau (khách trước xuống, khách sau lên); trip khác giữ nguyên cấm trùng.
        var seatedSegments = itemSegments
            .Where(x => !string.IsNullOrWhiteSpace(x.Item.SeatNumber))
            .Select(x => (SeatCode: NormalizeSeatCode(x.Item.SeatNumber!), x.FromStop, x.ToStop))
            .ToList();
        foreach (var seatGroup in seatedSegments.GroupBy(x => x.SeatCode).Where(g => g.Count() > 1))
        {
            var members = seatGroup.ToList();
            var hasConflict = !usesSegments || members
                .SelectMany((a, i) => members.Skip(i + 1).Select(b => (a, b)))
                .Any(p => BookingSeatOccupancySupport.SegmentsOverlap(
                    p.a.FromStop.StopOrder, p.a.ToStop.StopOrder,
                    p.b.FromStop.StopOrder, p.b.ToStop.StopOrder));
            if (hasConflict)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                    $"Seat '{seatGroup.Key}' is duplicated in this booking.")]);
        }

        var requestedSeatCodes = seatedSegments
            .Select(x => x.SeatCode)
            .Distinct()
            .ToList();

        var seatsByCode = await _context.Set<Seat>()
            .Where(x => x.BoatId == trip.BoatId.Value && requestedSeatCodes.Contains(x.Code))
            .Include(x => x.SeatType)
            .ToDictionaryAsync(x => x.Code, cancellationToken);

        var missingSeat = requestedSeatCodes.FirstOrDefault(x => !seatsByCode.ContainsKey(x));
        if (missingSeat is not null)
            throw new NotFoundException($"Seat '{missingSeat}' not found on this trip boat.");

        var invalidSeat = seatsByCode.Values.FirstOrDefault(x => !x.IsActive);
        if (invalidSeat is not null)
            throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                $"Seat '{invalidSeat.Code}' is not available for booking.")]);

        var requestedSeatIds = seatsByCode.Values.Select(x => x.Id).ToList();
        var tripSeatsBySeatId = await _context.Set<TripSeat>()
            .Where(x => x.TripId == trip.Id && requestedSeatIds.Contains(x.SeatId))
            .ToDictionaryAsync(x => x.SeatId, cancellationToken);

        var missingSeatInTrip = seatsByCode.Values.FirstOrDefault(x => !tripSeatsBySeatId.ContainsKey(x.Id));
        if (missingSeatInTrip is not null)
            throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                $"Seat '{missingSeatInTrip.Code}' is not available on this trip.")]);

        // Ghế đã có vé: chỉ chặn khi vé đang chiếm ghế GIAO CHẶNG với item (trip Regular);
        // vé cũ / trip sightseeing không có stop order được coi là chiếm ghế cả trip.
        var requestedTripSeatIds = tripSeatsBySeatId.Values.Select(x => x.Id).ToList();
        var occupyingPassengers = await _context.Set<BookingPassenger>()
            .Where(x => x.TripSeatId.HasValue
                     && requestedTripSeatIds.Contains(x.TripSeatId.Value))
            .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
            .Select(x => new { TripSeatId = x.TripSeatId!.Value, x.FromStopOrder, x.ToStopOrder })
            .ToListAsync(cancellationToken);

        var heldSeats = await _seatHoldService.GetHeldSeatsAsync(trip.Id, cancellationToken);

        foreach (var (item, fromStop, toStop) in itemSegments)
        {
            if (string.IsNullOrWhiteSpace(item.SeatNumber))
                continue;

            var seat = seatsByCode[NormalizeSeatCode(item.SeatNumber)];
            var tripSeat = tripSeatsBySeatId[seat.Id];
            var (fromOrder, toOrder) = usesSegments
                ? (fromStop.StopOrder, toStop.StopOrder)
                : (BookingSeatOccupancySupport.FullTripFromOrder, BookingSeatOccupancySupport.FullTripToOrder);

            var occupied = occupyingPassengers.Any(p =>
                p.TripSeatId == tripSeat.Id
                && BookingSeatOccupancySupport.SegmentsOverlap(
                    p.FromStopOrder ?? int.MinValue, p.ToStopOrder ?? int.MaxValue, fromOrder, toOrder));
            if (occupied)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                    $"Seat '{seat.Code}' is already booked.")]);

            var heldByOther = heldSeats.TryGetValue(tripSeat.Id, out var holds)
                && holds.Any(h => h.UserId != holderId
                    && BookingSeatOccupancySupport.SegmentsOverlap(
                        h.FromStopOrder, h.ToStopOrder, fromOrder, toOrder));
            if (heldByOther)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                    $"Seat '{seat.Code}' is being held by another customer.")]);
        }

        var resolvedItems = new List<ResolvedItem>();
        foreach (var (item, fromStop, toStop) in itemSegments)
        {
            Seat? seat = null;
            TripSeat? tripSeat = null;
            if (!string.IsNullOrWhiteSpace(item.SeatNumber))
            {
                seat = seatsByCode[NormalizeSeatCode(item.SeatNumber)];
                tripSeat = tripSeatsBySeatId[seat.Id];
            }

            resolvedItems.Add(new ResolvedItem(
                item,
                ticketTypesByCode[TicketTypeCatalog.NormalizeCode(item.TicketTypeCode)],
                seat,
                tripSeat,
                fromStop,
                toStop));
        }

        // Giá: ghế STANDARD trên trip Regular tính theo quãng đường của chặng
        // (GET /api/fare-policy); tuyến chưa nhập đủ km thì fallback giá theo loại ghế
        // để không chặn booking. Ghế sightseeing giữ nguyên giá theo loại ghế.
        FarePolicyDto? farePolicy = null;
        var itemPrices = new List<(ResolvedItem Resolved, decimal UnitPrice)>();
        foreach (var resolved in resolvedItems)
        {
            decimal unitPrice;
            if (resolved.Seat is null)
            {
                // Hành khách không chiếm ghế (INFANT ngồi cùng người lớn) → miễn phí.
                unitPrice = 0m;
            }
            else
            {
                var distanceKm = usesSegments && resolved.Seat.SeatTypeCode.Equals(
                        DistanceFareSupport.DistanceFareSeatTypeCode, StringComparison.OrdinalIgnoreCase)
                    ? DistanceFareSupport.TryComputeSegmentDistanceKm(
                        trip.Route.RouteStops, resolved.FromStop.StopOrder, resolved.ToStop.StopOrder)
                    : null;

                if (distanceKm.HasValue)
                {
                    farePolicy ??= await DistanceFareSupport.GetActivePolicyAsync(_context, cancellationToken);
                    unitPrice = DistanceFareSupport.CalculateFare(farePolicy, distanceKm.Value)
                        * resolved.TicketType.PriceModifier;
                }
                else
                {
                    unitPrice = await _fareCalculator.CalculateAsync(
                        resolved.Seat.Id,
                        resolved.TicketType.Code,
                        cancellationToken,
                        trip.Id);
                }
            }

            itemPrices.Add((resolved, unitPrice));
        }

        return new ResolvedLeg(trip, itemPrices);
    }

    /// <summary>
    /// Hai chiều trùng một trip: chặn ghế trùng giữa hai chiều — occupancy check chạy trước khi
    /// insert nên không tự bắt được trường hợp double-book cùng TripSeat trong cùng một lệnh.
    /// Trip Regular bán ghế theo chặng: chỉ chặn khi hai chặng giao nhau.
    /// </summary>
    public static void EnsureLegsDoNotShareSeats(ResolvedLeg outboundLeg, ResolvedLeg returnLeg)
    {
        if (returnLeg.Trip.Id != outboundLeg.Trip.Id)
        {
            return;
        }

        var sameTripUsesSegments = DistanceFareSupport.UsesDistanceFare(outboundLeg.Trip);
        var outboundSeated = outboundLeg.ItemPrices
            .Where(x => x.Resolved.Seat is not null)
            .Select(x => x.Resolved)
            .ToList();
        var overlappingSeat = returnLeg.ItemPrices
            .Where(x => x.Resolved.Seat is not null)
            .Select(x => x.Resolved)
            .FirstOrDefault(r => outboundSeated.Any(o =>
                o.Seat!.Code == r.Seat!.Code
                && (!sameTripUsesSegments || BookingSeatOccupancySupport.SegmentsOverlap(
                    o.FromStop.StopOrder, o.ToStop.StopOrder,
                    r.FromStop.StopOrder, r.ToStop.StopOrder))));
        if (overlappingSeat is not null)
            throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                $"Seat '{overlappingSeat.Seat!.Code}' is duplicated in this booking.")]);
    }

    /// <summary>Dựng BookingPassenger cho mọi chiều của booking (chiều đi + chiều về nếu có).</summary>
    public static void AddPassengers(Booking booking, IEnumerable<ResolvedLeg> legs)
    {
        foreach (var leg in legs)
        {
            // Stop order chỉ lưu cho trip bán ghế theo chặng (Regular) — passenger sightseeing
            // giữ null = chiếm ghế cả trip như trước.
            var legUsesSegments = DistanceFareSupport.UsesDistanceFare(leg.Trip);
            foreach (var x in leg.ItemPrices)
            {
                booking.Passengers.Add(new BookingPassenger
                {
                    BookingId = booking.Id,
                    FullName = x.Resolved.Item.PassengerName.Trim(),
                    PhoneNumber = x.Resolved.Item.PassengerPhone?.Trim(),
                    Email = string.IsNullOrWhiteSpace(x.Resolved.Item.PassengerEmail)
                        ? null
                        : x.Resolved.Item.PassengerEmail.Trim(),
                    PassengerType = x.Resolved.TicketType.Code,
                    BirthYear = x.Resolved.Item.BirthYear,
                    Gender = x.Resolved.Item.Gender?.Trim(),
                    Nationality = x.Resolved.Item.Nationality?.Trim(),
                    Note = x.Resolved.Item.Note?.Trim(),
                    TripId = leg.Trip.Id,
                    TripSeatId = x.Resolved.TripSeat?.Id,
                    UnitPrice = x.UnitPrice,
                    FromStationId = x.Resolved.FromStop.StationId,
                    ToStationId = x.Resolved.ToStop.StationId,
                    FromStopOrder = legUsesSegments ? x.Resolved.FromStop.StopOrder : null,
                    ToStopOrder = legUsesSegments ? x.Resolved.ToStop.StopOrder : null
                });
            }
        }
    }

    /// <summary>Ghế đã thành vé: nhả pre-hold của người đặt và báo sơ đồ ghế realtime là đã bán.</summary>
    public static async Task NotifyLegBookedAsync(
        ResolvedLeg leg,
        Guid holderId,
        ISeatHoldService seatHoldService,
        ITripSeatNotifier tripSeatNotifier,
        CancellationToken cancellationToken)
    {
        var seatedItems = leg.ItemPrices
            .Select(x => x.Resolved)
            .Where(x => x.Seat is not null && x.TripSeat is not null)
            .ToList();
        if (seatedItems.Count == 0)
        {
            return;
        }

        await seatHoldService.ReleaseAsync(
            leg.Trip.Id,
            seatedItems.Select(x => x.TripSeat!.Id).ToList(),
            holderId,
            cancellationToken);
        var legUsesSegments = DistanceFareSupport.UsesDistanceFare(leg.Trip);
        await tripSeatNotifier.PublishSeatStatusChangedAsync(
            leg.Trip.Id,
            seatedItems
                .Select(x => new TripSeatStatusChange(
                    x.Seat!.Code, "Booked",
                    legUsesSegments ? x.FromStop.StopOrder : null,
                    legUsesSegments ? x.ToStop.StopOrder : null))
                .ToList(),
            cancellationToken);
    }

    /// <summary>
    /// Khách tự đặt: chỉ chuyến Scheduled và còn trước hạn đóng bán. Bán tại quầy: staff bán được
    /// tới lúc chuyến kết thúc (kể cả tàu đã rời bến), chỉ chặn chuyến đã Completed/Cancelled.
    /// </summary>
    private static void EnsureTripSellable(
        Trip trip,
        DateTimeOffset now,
        string tripCodePropertyName,
        bool allowDepartedTrip)
    {
        if (!allowDepartedTrip)
        {
            if (trip.TripStatus != TripStatus.Scheduled)
                throw new ValidationException([new ValidationFailure(tripCodePropertyName,
                    "Trip is not available for booking.")]);

            if (trip.DepartureTime <= now + BookingExpirationPolicy.BookingCutoffBeforeDeparture)
                throw new ValidationException([new ValidationFailure(tripCodePropertyName,
                    $"Đã ngừng bán vé cho chuyến này (đóng bán trước {BookingExpirationPolicy.BookingCutoffBeforeDeparture.TotalMinutes:0} phút so với giờ khởi hành).")]);

            return;
        }

        if (trip.TripStatus is TripStatus.Completed or TripStatus.Cancelled)
            throw new ValidationException([new ValidationFailure(tripCodePropertyName,
                "Chuyến đã kết thúc hoặc đã hủy nên không bán vé được nữa.")]);
    }

    private static string NormalizeSeatCode(string value) => value.Trim().ToUpperInvariant();

    private static bool IsInfant(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "INFANT", StringComparison.OrdinalIgnoreCase);
}
