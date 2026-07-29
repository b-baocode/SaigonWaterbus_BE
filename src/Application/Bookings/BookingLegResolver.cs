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

/// <summary>Một ghế + chặng cần chiếm. Chặng full trip dùng cho vé sightseeing.</summary>
internal sealed record SeatSegmentRequest(Guid TripSeatId, string SeatCode, int FromStopOrder, int ToStopOrder);

/// <summary>
/// Resolve một chiều của booking thường: kiểm tra chuyến, loại vé, ghế (trùng chặng, đã bán,
/// đang pre-hold) và tính giá. Dùng chung cho khách tự đặt online (<see cref="CreateBookingCommand"/>)
/// và cho staff bán vé tại quầy (<see cref="CreateCounterBookingCommand"/>).
/// </summary>
internal sealed class BookingLegResolver
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

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
    /// true = bán vé tại quầy: bỏ hạn đóng bán trước giờ chạy và cho bán cả khi tàu đang chạy.
    /// false = khách tự đặt: cho các trạng thái còn vận hành và còn trước hạn đóng bán theo bến lên.
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

        // TripStops cần cho hạn đóng bán theo bến khách lên (giờ dự kiến từng bến đã chốt khi tạo
        // chuyến); trip cũ chưa có trip_stops thì suy từ RouteStops.
        var trip = await _context.Set<Trip>()
            .Include(t => t.TripStops)
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(t => t.TripCode == tripCode, cancellationToken)
            ?? throw new NotFoundException($"Trip '{tripCode}' not found.");

        if (trip.TripType != TripTypes.Regular)
            throw new ValidationException([new ValidationFailure(tripCodePropertyName,
                "Trip charter không bán vé lẻ.")]);

        EnsureTripSellable(trip, tripCodePropertyName, allowDepartedTrip);

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

        var currentYear = now.ToOffset(VietnamOffset).Year;
        var futureBirthYearItem = items.FirstOrDefault(i => i.BirthYear.HasValue && i.BirthYear.Value > currentYear);
        if (futureBirthYearItem is not null)
        {
            throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.BirthYear),
                $"birthYear không được lớn hơn năm hiện tại ({currentYear}).")]);
        }

        // Vé INFANT chỉ dành cho trẻ dưới 2 tuổi — kiểm tra năm sinh so với ngày khởi hành chuyến.
        var departureYear = trip.DepartureTime.ToOffset(VietnamOffset).Year;
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

        // Vé CHILD dành cho trẻ trên 2 tuổi đến 12 tuổi, miễn phí, có ghế riêng và có QR riêng.
        foreach (var childItem in items.Where(i => IsChild(i.TicketTypeCode)))
        {
            if (!childItem.BirthYear.HasValue)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.BirthYear),
                    "Vé CHILD yêu cầu khai báo birthYear (trẻ trên 2 tuổi đến 12 tuổi).")]);

            var ageAtDeparture = departureYear - childItem.BirthYear.Value;
            if (ageAtDeparture <= 2 || ageAtDeparture > 12)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.BirthYear),
                    "Vé CHILD chỉ áp dụng cho trẻ trên 2 tuổi đến 12 tuổi tại ngày khởi hành.")]);
        }

        // Vé SENIOR và DISABLED cũng cần năm sinh để lưu manifest hành khách.
        foreach (var seniorItem in items.Where(i => IsSenior(i.TicketTypeCode)))
        {
            if (!seniorItem.BirthYear.HasValue)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.BirthYear),
                    "Vé SENIOR yêu cầu khai báo birthYear (người cao tuổi từ 70 tuổi).")]);

            var ageAtDeparture = departureYear - seniorItem.BirthYear.Value;
            if (ageAtDeparture < 70)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.BirthYear),
                    "Vé SENIOR chỉ áp dụng cho người cao tuổi từ 70 tuổi tại ngày khởi hành.")]);
        }

        foreach (var disabledItem in items.Where(i => IsDisabled(i.TicketTypeCode)))
        {
            if (!disabledItem.BirthYear.HasValue)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.BirthYear),
                    "Vé DISABLED yêu cầu khai báo birthYear (người khuyết tật).")]);
        }

        // Trẻ dưới 2 tuổi (INFANT) được phép không chiếm ghế (ngồi cùng người lớn) và dùng QR
        // của một hành khách ADULT đi kèm. CHILD có ghế riêng nên vẫn được phát QR riêng.
        var lapItems = items.Where(i => string.IsNullOrWhiteSpace(i.SeatNumber)).ToList();
        if (lapItems.Count > 0)
        {
            var nonInfantLapItem = lapItems.FirstOrDefault(i => !IsInfant(i.TicketTypeCode));
            if (nonInfantLapItem is not null)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                    "Chỉ vé INFANT (trẻ dưới 2 tuổi) mới được bỏ trống seatNumber.")]);

            // Check nhanh theo số lượng; check đúng cùng chiều/cùng chặng chạy sau khi resolve itemSegments.
            var seatedCompanionCount = items.Count(i =>
                !string.IsNullOrWhiteSpace(i.SeatNumber) && IsAdult(i.TicketTypeCode));
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
                var segment = Trips.TripSegmentSupport.ResolveOpenOrFirst(
                    trip,
                    item.FromStationCode,
                    item.ToStationCode,
                    nameof(BookingItemRequest.FromStationCode),
                    nameof(BookingItemRequest.ToStationCode),
                    now);

                itemSegments.Add((item, segment.FromStop!, segment.ToStop!));
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

        EnsureChildAndLapInfantAdultRules(itemSegments);

        // Hạn đóng bán theo bến khách LÊN (không phải bến đầu tuyến) — chỉ áp cho khách tự đặt;
        // staff bán tại quầy vẫn bán được cả khi tàu đã rời bến.
        if (!allowDepartedTrip)
        {
            EnsureSegmentsWithinCutoff(trip, itemSegments, now, tripCodePropertyName);
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

        var seatedRequests = itemSegments
            .Where(x => !string.IsNullOrWhiteSpace(x.Item.SeatNumber))
            .Select(x =>
            {
                var seat = seatsByCode[NormalizeSeatCode(x.Item.SeatNumber!)];
                var (fromOrder, toOrder) = usesSegments
                    ? (x.FromStop.StopOrder, x.ToStop.StopOrder)
                    : (BookingSeatOccupancySupport.FullTripFromOrder, BookingSeatOccupancySupport.FullTripToOrder);
                return new SeatSegmentRequest(tripSeatsBySeatId[seat.Id].Id, seat.Code, fromOrder, toOrder);
            })
            .ToList();

        await EnsureSeatsAvailableAsync(trip.Id, seatedRequests, holderId, now, cancellationToken);

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

            var ticketType = ticketTypesByCode[TicketTypeCatalog.NormalizeCode(item.TicketTypeCode)];
            if (seat is not null && !ticketType.IsApplicableForSeatType(seat.SeatTypeCode))
            {
                var allowed = ticketType.AllowedSeatTypeCodes!;
                throw new ValidationException(
                [
                    new ValidationFailure(nameof(BookingItemRequest.TicketTypeCode),
                        $"Loại vé '{ticketType.Name}' chỉ áp dụng cho ghế: {allowed}. "
                        + $"Ghế '{seat.Code}' là loại {seat.SeatTypeCode}.")
                ]);
            }

            resolvedItems.Add(new ResolvedItem(
                item,
                ticketType,
                seat,
                tripSeat,
                fromStop,
                toStop));
        }

        // Giá: ghế STANDARD trên trip Regular tính theo quãng đường của chặng
        // (GET /api/fare-policy). Nếu tuyến chưa nhập đủ km thì chặn bán để tránh lấy nhầm
        // giá loại ghế STANDARD legacy. Phụ thu cuối tuần/lễ/đặc biệt áp theo ngày chạy của trip.
        FarePolicyDto? farePolicy = null;
        var fareAdjustment = await FareAdjustmentSupport.GetEffectiveAdjustmentAsync(
            _context, trip.OperatingDate, cancellationToken);
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
                var isDistanceFareSeat = usesSegments && resolved.Seat.SeatTypeCode.Equals(
                    DistanceFareSupport.DistanceFareSeatTypeCode, StringComparison.OrdinalIgnoreCase);

                if (isDistanceFareSeat)
                {
                    var distanceKm = DistanceFareSupport.TryComputeSegmentDistanceKm(
                        trip.Route.RouteStops, resolved.FromStop.StopOrder, resolved.ToStop.StopOrder);
                    if (!distanceKm.HasValue)
                    {
                        throw new ValidationException(
                        [
                            new ValidationFailure(
                                DistanceFareSupport.MissingDistancePropertyName,
                                DistanceFareSupport.MissingDistanceReason)
                        ]);
                    }

                    farePolicy ??= await DistanceFareSupport.GetActivePolicyAsync(_context, cancellationToken);
                    var baseFare = DistanceFareSupport.CalculateFare(farePolicy, distanceKm.Value);
                    var priceModifier = await TicketFareRuleSupport.GetEffectivePriceModifierAsync(
                        _context,
                        resolved.TicketType,
                        trip.Route.RouteType,
                        cancellationToken);
                    unitPrice = FareAdjustmentSupport.ApplySurcharge(baseFare, fareAdjustment)
                        * priceModifier;
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

    /// <summary>
    /// Booking chỉ được giữ tối đa 15 phút và không được vượt hạn đóng bán của bất kỳ chặng nào
    /// trong đơn. Ví dụ tàu rời bến lên 20:47, đóng bán trước 10 phút, thì hold muộn nhất là 20:37.
    /// </summary>
    public static DateTimeOffset ResolveHoldExpiresAt(IEnumerable<ResolvedLeg> legs, DateTimeOffset now)
    {
        var standardHoldExpiresAt = now.Add(BookingSeatOccupancySupport.BookingHoldDuration);
        var segmentDeadline = legs
            .SelectMany(leg => leg.ItemPrices.Select(x =>
                Trips.BookingCutoffSupport.ResolveBookingDeadline(
                    leg.Trip,
                    x.Resolved.FromStop.StopOrder,
                    x.Resolved.ToStop.StopOrder)))
            .DefaultIfEmpty(standardHoldExpiresAt)
            .Min();

        return segmentDeadline < standardHoldExpiresAt ? segmentDeadline : standardHoldExpiresAt;
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

    /// <summary>
    /// Ghế đã có vé: chỉ chặn khi vé đang chiếm ghế GIAO CHẶNG với item (trip Regular); vé cũ /
    /// trip sightseeing không có stop order được coi là chiếm ghế cả trip. Ngoài ra chặn ghế đang
    /// được KHÁCH KHÁC pre-hold trên chặng giao nhau.
    ///
    /// Chạy hai lần: lần đầu lúc resolve (báo lỗi sớm, chưa tốn transaction), lần hai bên trong
    /// transaction sau khi đã khoá trip_seats — xem <see cref="EnsureSeatsStillAvailableAsync"/>.
    /// </summary>
    public async Task EnsureSeatsAvailableAsync(
        Guid tripId,
        IReadOnlyList<SeatSegmentRequest> seatedRequests,
        Guid holderId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (seatedRequests.Count == 0)
        {
            return;
        }

        var tripSeatIds = seatedRequests.Select(x => x.TripSeatId).Distinct().ToList();
        var occupyingPassengers = await _context.Set<BookingPassenger>()
            .Where(x => x.TripSeatId.HasValue && tripSeatIds.Contains(x.TripSeatId.Value))
            .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
            .Select(x => new { TripSeatId = x.TripSeatId!.Value, x.FromStopOrder, x.ToStopOrder })
            .ToListAsync(cancellationToken);

        var heldSeats = await _seatHoldService.GetHeldSeatsAsync(tripId, cancellationToken);

        foreach (var request in seatedRequests)
        {
            var occupied = occupyingPassengers.Any(p =>
                p.TripSeatId == request.TripSeatId
                && BookingSeatOccupancySupport.SegmentsOverlap(
                    p.FromStopOrder ?? int.MinValue, p.ToStopOrder ?? int.MaxValue,
                    request.FromStopOrder, request.ToStopOrder));
            if (occupied)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                    $"Seat '{request.SeatCode}' is already booked.")]);

            var heldByOther = heldSeats.TryGetValue(request.TripSeatId, out var holds)
                && holds.Any(h => h.UserId != holderId
                    && BookingSeatOccupancySupport.SegmentsOverlap(
                        h.FromStopOrder, h.ToStopOrder, request.FromStopOrder, request.ToStopOrder));
            if (heldByOther)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                    $"Seat '{request.SeatCode}' is being held by another customer.")]);
        }
    }

    /// <summary>
    /// Chốt ghế bên trong transaction: khoá hàng trip_seats rồi kiểm tra lại. Hai request song song
    /// cùng ghế sẽ bị tuần tự hoá tại đây, request sau đọc được vé của request trước và bị chặn —
    /// nếu không, cả hai đều đọc thấy "trống" ở bước resolve và cùng insert.
    /// </summary>
    public static async Task EnsureSeatsStillAvailableAsync(
        IApplicationDbContext context,
        BookingLegResolver resolver,
        IReadOnlyList<ResolvedLeg> legs,
        Guid holderId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var seatedByTrip = legs
            .Select(leg => (leg.Trip, Seats: SeatSegmentsOf(leg)))
            .Where(x => x.Seats.Count > 0)
            .ToList();
        if (seatedByTrip.Count == 0)
        {
            return;
        }

        await context.LockTripSeatsAsync(
            seatedByTrip.SelectMany(x => x.Seats).Select(x => x.TripSeatId).ToList(),
            cancellationToken);

        foreach (var (trip, seats) in seatedByTrip)
        {
            await resolver.EnsureSeatsAvailableAsync(trip.Id, seats, holderId, now, cancellationToken);
        }
    }

    private static List<SeatSegmentRequest> SeatSegmentsOf(ResolvedLeg leg)
    {
        var usesSegments = DistanceFareSupport.UsesDistanceFare(leg.Trip);
        return leg.ItemPrices
            .Select(x => x.Resolved)
            .Where(x => x.Seat is not null && x.TripSeat is not null)
            .Select(x => new SeatSegmentRequest(
                x.TripSeat!.Id,
                x.Seat!.Code,
                usesSegments ? x.FromStop.StopOrder : BookingSeatOccupancySupport.FullTripFromOrder,
                usesSegments ? x.ToStop.StopOrder : BookingSeatOccupancySupport.FullTripToOrder))
            .ToList();
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
    /// Khách tự đặt: cho các trạng thái còn vận hành và còn trước hạn đóng bán theo bến lên.
    /// Bán tại quầy: staff bán được tới lúc chuyến kết thúc, chỉ chặn chuyến đã Completed/Cancelled.
    /// </summary>
    /// <summary>
    /// Mỗi vé phải còn trước hạn đóng bán tính theo giờ tàu rời BẾN KHÁCH LÊN. Một booking có
    /// nhiều chặng khác nhau thì xét từng vé — chặng lên sớm có thể đã đóng bán trong khi chặng
    /// lên muộn vẫn mở.
    /// </summary>
    private static void EnsureSegmentsWithinCutoff(
        Trip trip,
        IReadOnlyList<(BookingItemRequest Item, RouteStop FromStop, RouteStop ToStop)> itemSegments,
        DateTimeOffset now,
        string tripCodePropertyName)
    {
        var closedSegment = itemSegments.FirstOrDefault(x =>
            Trips.BookingCutoffSupport.IsPastCutoff(trip, x.FromStop.StopOrder, x.ToStop.StopOrder, now));
        if (closedSegment.Item is not null)
            throw new ValidationException([new ValidationFailure(tripCodePropertyName,
                Trips.BookingCutoffSupport.CutoffMessage())]);
    }

    private static void EnsureChildAndLapInfantAdultRules(
        IReadOnlyList<(BookingItemRequest Item, RouteStop FromStop, RouteStop ToStop)> itemSegments)
    {
        var adults = itemSegments
            .Where(x => IsAdult(x.Item.TicketTypeCode) && !string.IsNullOrWhiteSpace(x.Item.SeatNumber))
            .OrderBy(x => x.FromStop.StopOrder)
            .ThenBy(x => x.ToStop.StopOrder)
            .ThenBy(x => NormalizeSeatCode(x.Item.SeatNumber!))
            .ToList();

        var childWithoutAdult = itemSegments
            .Where(x => IsChild(x.Item.TicketTypeCode))
            .FirstOrDefault(child => !adults.Any(adult =>
                adult.FromStop.StopOrder == child.FromStop.StopOrder
                && adult.ToStop.StopOrder == child.ToStop.StopOrder));
        if (childWithoutAdult.Item is not null)
        {
            throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.TicketTypeCode),
                "Vé CHILD miễn phí phải có ít nhất một hành khách ADULT có ghế đi kèm cùng chiều và cùng chặng.")]);
        }

        var usedAdultIndexes = new HashSet<int>();
        foreach (var infant in itemSegments
                     .Where(x => IsLapInfantRequest(x.Item))
                     .OrderBy(x => x.FromStop.StopOrder)
                     .ThenBy(x => x.ToStop.StopOrder)
                     .ThenBy(x => x.Item.PassengerName))
        {
            var adultIndex = -1;
            for (var i = 0; i < adults.Count; i++)
            {
                var adult = adults[i];
                if (!usedAdultIndexes.Contains(i)
                    && adult.FromStop.StopOrder == infant.FromStop.StopOrder
                    && adult.ToStop.StopOrder == infant.ToStop.StopOrder)
                {
                    adultIndex = i;
                    break;
                }
            }
            if (adultIndex < 0)
            {
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.TicketTypeCode),
                    "Mỗi INFANT không chiếm ghế phải có một hành khách ADULT có ghế "
                    + "đi kèm cùng chiều và cùng chặng.")]);
            }

            usedAdultIndexes.Add(adultIndex);
        }
    }

    private static void EnsureTripSellable(
        Trip trip,
        string tripCodePropertyName,
        bool allowDepartedTrip)
    {
        if (!allowDepartedTrip)
        {
            if (!Trips.TripBookingAvailabilitySupport.CanAcceptCustomerBooking(trip.TripStatus))
                throw new ValidationException([new ValidationFailure(tripCodePropertyName,
                    "Trip is not available for booking.")]);

            // Hạn đóng bán KHÔNG kiểm ở đây: nó tính theo bến khách lên, mà chặng chỉ resolve
            // được sau bước này — xem EnsureSegmentsWithinCutoff.
            return;
        }

        if (trip.TripStatus is TripStatus.Completed or TripStatus.Cancelled)
            throw new ValidationException([new ValidationFailure(tripCodePropertyName,
                "Chuyến đã kết thúc hoặc đã hủy nên không bán vé được nữa.")]);
    }

    private static string NormalizeSeatCode(string value) => value.Trim().ToUpperInvariant();

    private static bool IsInfant(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "INFANT", StringComparison.OrdinalIgnoreCase);

    private static bool IsChild(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "CHILD", StringComparison.OrdinalIgnoreCase);

    private static bool IsSenior(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "SENIOR", StringComparison.OrdinalIgnoreCase);

    private static bool IsDisabled(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "DISABLED", StringComparison.OrdinalIgnoreCase);

    private static bool IsAdult(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "ADULT", StringComparison.OrdinalIgnoreCase);

    private static bool IsLapInfantRequest(BookingItemRequest item) =>
        IsInfant(item.TicketTypeCode) && string.IsNullOrWhiteSpace(item.SeatNumber);
}
