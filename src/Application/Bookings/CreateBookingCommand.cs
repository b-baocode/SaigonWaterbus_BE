using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

public sealed record BookingItemRequest(
    string? SeatNumber,
    string TicketTypeCode,
    string FromStationCode,
    string ToStationCode,
    string PassengerName,
    string? PassengerPhone,
    int? BirthYear,
    string? Gender,
    string? Nationality,
    string? Note,
    string? PassengerEmail = null);

public sealed record CreateBookingResult(
    Guid BookingId,
    string BookingCode,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string BookingStatus,
    int ItemCount,
    DateTimeOffset? HoldExpiresAt);

public sealed record CreateBookingCommand(
    string TripCode,
    IReadOnlyList<BookingItemRequest> Items,
    string? PromotionCode) : IRequest<CreateBookingResult>;

public sealed class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator()
    {
        RuleFor(x => x.TripCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Booking must have at least one item.")
            .Must(items => items.Count <= 10).WithMessage("Maximum 10 items per booking.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.TicketTypeCode).NotEmpty().MaximumLength(50);
            item.RuleFor(x => x.SeatNumber).MaximumLength(30);
            item.RuleFor(x => x.SeatNumber).NotEmpty()
                .When(x => !IsInfant(x.TicketTypeCode))
                .WithMessage("seatNumber là bắt buộc (chỉ vé INFANT - trẻ dưới 2 tuổi - mới được bỏ trống để ngồi cùng người lớn).");
            item.RuleFor(x => x.BirthYear).NotNull()
                .When(x => IsInfant(x.TicketTypeCode))
                .WithMessage("birthYear là bắt buộc với vé INFANT (trẻ dưới 2 tuổi) để khai báo và lưu hành khách.");
            item.RuleFor(x => x.FromStationCode).NotEmpty().MaximumLength(50);
            item.RuleFor(x => x.ToStationCode).NotEmpty().MaximumLength(50)
                .NotEqual(x => x.FromStationCode).WithMessage("From and To stations must be different.");
            item.RuleFor(x => x.PassengerName).NotEmpty().MaximumLength(150);
            item.RuleFor(x => x.PassengerPhone).MaximumLength(20).When(x => x.PassengerPhone is not null);
            item.RuleFor(x => x.PassengerEmail).EmailAddress().MaximumLength(255)
                .When(x => !string.IsNullOrWhiteSpace(x.PassengerEmail));
        });

        // Trẻ dưới 2 tuổi ngồi cùng người lớn (INFANT không chiếm ghế) phải có người lớn có ghế đi kèm.
        // Mỗi hành khách có ghế (không phải INFANT) chỉ "kèm" tối đa một trẻ ngồi lòng.
        RuleFor(x => x.Items)
            .Must(items =>
            {
                var lapInfants = items.Count(i =>
                    string.IsNullOrWhiteSpace(i.SeatNumber) && IsInfant(i.TicketTypeCode));
                var seatedCompanions = items.Count(i =>
                    !string.IsNullOrWhiteSpace(i.SeatNumber) && !IsInfant(i.TicketTypeCode));
                return lapInfants <= seatedCompanions;
            })
            .WithMessage("Mỗi trẻ dưới 2 tuổi (INFANT không chiếm ghế) phải có một hành khách người lớn có ghế đi kèm trong cùng booking.")
            .When(x => x.Items is not null);
    }

    private static bool IsInfant(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "INFANT", StringComparison.OrdinalIgnoreCase);
}

public sealed class CreateBookingCommandHandler : IRequestHandler<CreateBookingCommand, CreateBookingResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBookingCodeGenerator _bookingCodeGenerator;
    private readonly IFareCalculator _fareCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly ISeatHoldService _seatHoldService;
    private readonly ITripSeatNotifier _tripSeatNotifier;
    private readonly IPromotionLock? _promotionLock;

    public CreateBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBookingCodeGenerator bookingCodeGenerator,
        IFareCalculator fareCalculator,
        TimeProvider timeProvider,
        ISeatHoldService? seatHoldService = null,
        ITripSeatNotifier? tripSeatNotifier = null,
        IPromotionLock? promotionLock = null)
    {
        _context = context;
        _userContext = userContext;
        _bookingCodeGenerator = bookingCodeGenerator;
        _fareCalculator = fareCalculator;
        _timeProvider = timeProvider;
        _seatHoldService = seatHoldService ?? NullSeatHoldService.Instance;
        _tripSeatNotifier = tripSeatNotifier ?? NullTripSeatNotifier.Instance;
        _promotionLock = promotionLock;
    }

    public async Task<CreateBookingResult> Handle(CreateBookingCommand request, CancellationToken cancellationToken)
    {
        var tripCode = request.TripCode.Trim().ToUpperInvariant();

        var trip = await _context.Set<Trip>()
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(t => t.TripCode == tripCode, cancellationToken)
            ?? throw new NotFoundException($"Trip '{tripCode}' not found.");

        var now = _timeProvider.GetUtcNow();
        if (trip.TripType != TripTypes.Regular)
            throw new ValidationException([new ValidationFailure(nameof(request.TripCode),
                "Trip charter không bán vé lẻ.")]);

        if (trip.TripStatus != TripStatus.Scheduled || trip.DepartureTime <= now)
            throw new ValidationException([new ValidationFailure(nameof(request.TripCode),
                "Trip is not available for booking.")]);

        if (!trip.BoatId.HasValue)
            throw new ValidationException([new ValidationFailure(nameof(request.TripCode),
                "Trip has no boat assigned.")]);

        var requestedTicketCodes = request.Items
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
        foreach (var infantItem in request.Items.Where(i => IsInfant(i.TicketTypeCode)))
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
        var lapItems = request.Items.Where(i => string.IsNullOrWhiteSpace(i.SeatNumber)).ToList();
        if (lapItems.Count > 0)
        {
            var nonInfantLapItem = lapItems.FirstOrDefault(i => !IsInfant(i.TicketTypeCode));
            if (nonInfantLapItem is not null)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                    "Chỉ vé INFANT (trẻ dưới 2 tuổi) mới được bỏ trống seatNumber.")]);

            // Mỗi trẻ ngồi lòng (INFANT không ghế) phải có một hành khách có ghế (không phải INFANT) đi kèm
            // trong cùng booking — 1 người lớn kèm tối đa 1 trẻ. (Cũng khai báo ở validator, nhưng validator
            // không chạy trong pipeline MediatR nên bắt buộc phải enforce tại handler.)
            var seatedCompanionCount = request.Items.Count(i =>
                !string.IsNullOrWhiteSpace(i.SeatNumber) && !IsInfant(i.TicketTypeCode));
            if (lapItems.Count > seatedCompanionCount)
                throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                    "Mỗi trẻ dưới 2 tuổi (INFANT không chiếm ghế) phải có một hành khách người lớn có ghế "
                    + "đi kèm trong cùng booking.")]);
        }

        var requestedSeatCodes = request.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.SeatNumber))
            .Select(i => NormalizeSeatCode(i.SeatNumber!))
            .ToList();

        var duplicatedSeatCode = requestedSeatCodes
            .GroupBy(x => x)
            .FirstOrDefault(x => x.Count() > 1)
            ?.Key;
        if (duplicatedSeatCode is not null)
            throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                $"Seat '{duplicatedSeatCode}' is duplicated in this booking.")]);

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

        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var requestedTripSeatIds = tripSeatsBySeatId.Values.Select(x => x.Id).ToList();
        var occupiedTripSeatIds = await _context.Set<BookingPassenger>()
            .Where(x => x.TripSeatId.HasValue
                     && requestedTripSeatIds.Contains(x.TripSeatId.Value))
            .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
            .Select(x => x.TripSeatId!.Value)
            .ToListAsync(cancellationToken);

        if (occupiedTripSeatIds.Count > 0)
        {
            var occupiedSeat = seatsByCode.Values.First(x =>
                tripSeatsBySeatId.TryGetValue(x.Id, out var ts) && occupiedTripSeatIds.Contains(ts.Id));
            throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                $"Seat '{occupiedSeat.Code}' is already booked.")]);
        }

        var heldSeats = await _seatHoldService.GetHeldSeatsAsync(trip.Id, cancellationToken);
        var seatHeldByOther = seatsByCode.Values.FirstOrDefault(x =>
            tripSeatsBySeatId.TryGetValue(x.Id, out var ts)
            && heldSeats.TryGetValue(ts.Id, out var holder)
            && holder != userId);
        if (seatHeldByOther is not null)
        {
            throw new ValidationException([new ValidationFailure(nameof(BookingItemRequest.SeatNumber),
                $"Seat '{seatHeldByOther.Code}' is being held by another customer.")]);
        }

        var routeStopByStationCode = trip.Route.RouteStops
            .ToDictionary(
                rs => rs.Station.StationCode.ToUpperInvariant(),
                rs => rs);

        var resolvedItems = new List<ResolvedItem>();
        foreach (var item in request.Items)
        {
            var fromCode = item.FromStationCode.Trim().ToUpperInvariant();
            var toCode = item.ToStationCode.Trim().ToUpperInvariant();

            if (!routeStopByStationCode.TryGetValue(fromCode, out var fromStop))
                throw new ValidationException([new ValidationFailure(nameof(item.FromStationCode),
                    $"Station '{fromCode}' is not a stop on this trip.")]);

            if (!routeStopByStationCode.TryGetValue(toCode, out var toStop))
                throw new ValidationException([new ValidationFailure(nameof(item.ToStationCode),
                    $"Station '{toCode}' is not a stop on this trip.")]);

            if (fromStop.StopOrder >= toStop.StopOrder)
                throw new ValidationException([new ValidationFailure(nameof(item.FromStationCode),
                    $"Station '{fromCode}' must come before '{toCode}' on the route.")]);

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

        var itemPrices = new List<(ResolvedItem Resolved, decimal UnitPrice)>();
        foreach (var resolved in resolvedItems)
        {
            // Hành khách không chiếm ghế (INFANT ngồi cùng người lớn) → miễn phí, không tính giá ghế.
            var unitPrice = resolved.Seat is null
                ? 0m
                : await _fareCalculator.CalculateAsync(
                    resolved.Seat.Id,
                    resolved.TicketType.Code,
                    cancellationToken,
                    trip.Id);

            itemPrices.Add((resolved, unitPrice));
        }

        var subtotal = itemPrices.Sum(x => x.UnitPrice);

        var user = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new ValidationException([new ValidationFailure("userId", "User không tồn tại.")]);

        var booking = new Booking
        {
            UserId = userId,
            TripId = trip.Id,
            BookingCode = await _bookingCodeGenerator.GenerateAsync(cancellationToken),
            ContactName = user.FullName,
            ContactPhone = user.PhoneNumber ?? string.Empty,
            ContactEmail = user.Email,
            BookingStatus = BookingStatus.PendingPayment,
            SubtotalAmount = subtotal,
            DiscountAmount = 0,
            TotalAmount = subtotal,
            HoldExpiresAt = now.Add(BookingSeatOccupancySupport.BookingHoldDuration)
        };

        foreach (var x in itemPrices)
        {
            var passenger = new BookingPassenger
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
                TripSeatId = x.Resolved.TripSeat?.Id,
                UnitPrice = x.UnitPrice
            };
            booking.Passengers.Add(passenger);
        }

        _context.Set<Booking>().Add(booking);

        var applyContext = new PromotionApplyContext(
            Booking.SeatBookingType,
            trip.RouteId,
            trip.OperatingDate.DayOfWeek,
            TimeOnly.FromTimeSpan(trip.DepartureTime.TimeOfDay));

        try
        {
            await _context.ExecuteInTransactionAsync(
                async ct =>
                {
                    // Khoá + kiểm tra + tính giảm giá dưới cùng transaction để không vượt hạn mức khi nhiều đơn cùng dùng một mã.
                    if (!string.IsNullOrWhiteSpace(request.PromotionCode))
                    {
                        var code = PromotionSupport.NormalizeCode(request.PromotionCode);
                        var promotion = _promotionLock is not null
                            ? await _promotionLock.AcquireByCodeAsync(code, ct)
                            : await _context.Set<Promotion>().SingleOrDefaultAsync(p => p.PromotionCode == code, ct);

                        if (promotion is null)
                        {
                            throw new ValidationException([new ValidationFailure(
                                nameof(request.PromotionCode), "Không tìm thấy mã khuyến mãi.")]);
                        }

                        var discount = await PromotionEligibilitySupport.EnsureAndCalculateAsync(
                            _context, promotion, userId, subtotal, now,
                            nameof(request.PromotionCode), applyContext, null, ct);

                        booking.PromotionId = promotion.Id;
                        booking.DiscountAmount = discount;
                        booking.TotalAmount = subtotal - discount;
                    }

                    await _context.SaveChangesAsync(ct);
                },
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Items),
                "Booking failed. Please try again.")]);
        }

        var seatedItems = resolvedItems.Where(x => x.Seat is not null && x.TripSeat is not null).ToList();
        if (seatedItems.Count > 0)
        {
            await _seatHoldService.ReleaseAsync(
                trip.Id,
                seatedItems.Select(x => x.TripSeat!.Id).ToList(),
                userId,
                cancellationToken);
            await _tripSeatNotifier.PublishSeatStatusChangedAsync(
                trip.Id,
                seatedItems.Select(x => new TripSeatStatusChange(x.Seat!.Code, "Booked")).ToList(),
                cancellationToken);
        }

        return new CreateBookingResult(
            booking.Id, booking.BookingCode,
            booking.SubtotalAmount, booking.DiscountAmount, booking.TotalAmount,
            booking.BookingStatus.ToString(), booking.Passengers.Count,
            booking.HoldExpiresAt);
    }

    private sealed record ResolvedItem(
        BookingItemRequest Item,
        TicketTypeInfo TicketType,
        Seat? Seat,
        TripSeat? TripSeat,
        RouteStop FromStop,
        RouteStop ToStop);

    private static string NormalizeSeatCode(string value) => value.Trim().ToUpperInvariant();

    private static bool IsInfant(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "INFANT", StringComparison.OrdinalIgnoreCase);
}
