using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

// ─── DTOs ────────────────────────────────────────────────
public sealed record BookingItemRequest(
    string SeatNumber,
    string TicketTypeCode,
    string FromStationCode,
    string ToStationCode,
    string PassengerName,
    string? PassengerPhone);

public sealed record CreateBookingResult(
    Guid BookingId,
    string BookingCode,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string BookingStatus,
    int ItemCount);

// ─── Command ─────────────────────────────────────────────
public sealed record CreateBookingCommand(
    string TripCode,
    IReadOnlyList<BookingItemRequest> Items,
    string? PromotionCode) : IRequest<CreateBookingResult>;

// ─── Validator ───────────────────────────────────────────
public sealed class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator()
    {
        RuleFor(x => x.TripCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Booking must have at least one item.")
            .Must(items => items.Count <= 10).WithMessage("Maximum 10 items per booking.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.SeatNumber).NotEmpty().MaximumLength(20);
            item.RuleFor(x => x.TicketTypeCode).NotEmpty().MaximumLength(50);
            item.RuleFor(x => x.FromStationCode).NotEmpty().MaximumLength(50);
            item.RuleFor(x => x.ToStationCode).NotEmpty().MaximumLength(50)
                .NotEqual(x => x.FromStationCode).WithMessage("From and To stations must be different.");
            item.RuleFor(x => x.PassengerName).NotEmpty().MaximumLength(150);
            item.RuleFor(x => x.PassengerPhone).MaximumLength(20).When(x => x.PassengerPhone is not null);
        });
    }
}

// ─── Handler ─────────────────────────────────────────────
public sealed class CreateBookingCommandHandler : IRequestHandler<CreateBookingCommand, CreateBookingResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBookingCodeGenerator _bookingCodeGenerator;
    private readonly ISeatAvailabilityService _seatAvailability;
    private readonly IFareCalculator _fareCalculator;
    private readonly TimeProvider _timeProvider;

    public CreateBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBookingCodeGenerator bookingCodeGenerator,
        ISeatAvailabilityService seatAvailability,
        IFareCalculator fareCalculator,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _bookingCodeGenerator = bookingCodeGenerator;
        _seatAvailability = seatAvailability;
        _fareCalculator = fareCalculator;
        _timeProvider = timeProvider;
    }

    public async Task<CreateBookingResult> Handle(CreateBookingCommand request, CancellationToken cancellationToken)
    {
        var tripCode = request.TripCode.Trim().ToUpperInvariant();

        // 1. Load trip kèm TripStops → RouteStop → Station (để resolve StationCode)
        var trip = await _context.Set<Trip>()
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.RouteStop)
                    .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(t => t.TripCode == tripCode, cancellationToken)
            ?? throw new NotFoundException($"Trip '{tripCode}' not found.");

        // 2. Validate trip còn bookable
        var now = _timeProvider.GetUtcNow();
        if (trip.TripStatus != TripStatus.Scheduled || trip.DepartureTime <= now)
            throw new ValidationException([new ValidationFailure(nameof(request.TripCode),
                "Trip is not available for booking.")]);

        // 3. Resolve SeatNumber → Seat (unique trong boat)
        var requestedSeatNumbers = request.Items
            .Select(i => i.SeatNumber.Trim().ToUpperInvariant())
            .ToList();

        var seatsByNumber = await _context.Set<Seat>()
            .Where(s => s.BoatId == trip.BoatId
                     && requestedSeatNumbers.Contains(s.SeatNumber)
                     && s.IsActive)
            .ToDictionaryAsync(s => s.SeatNumber, cancellationToken);

        var missingSeat = requestedSeatNumbers.FirstOrDefault(n => !seatsByNumber.ContainsKey(n));
        if (missingSeat is not null)
            throw new NotFoundException($"Seat '{missingSeat}' not found on this boat.");

        // 4. Validate seat availability (dùng resolved SeatId)
        var requestedSeatIds = seatsByNumber.Values.Select(s => s.Id).ToList();
        var availableSeats = await _seatAvailability.GetAvailableSeatsAsync(trip.Id, requestedSeatIds, cancellationToken);
        if (requestedSeatIds.Except(availableSeats).Any())
            throw new ValidationException([new ValidationFailure(nameof(request.Items),
                "One or more seats are no longer available.")]);

        // 5. Resolve TicketTypeCode → TicketType
        var requestedTicketCodes = request.Items
            .Select(i => i.TicketTypeCode.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var ticketTypesByCode = await _context.Set<TicketType>()
            .Where(tt => requestedTicketCodes.Contains(tt.TicketTypeCode) && tt.IsActive)
            .ToDictionaryAsync(tt => tt.TicketTypeCode, cancellationToken);

        var missingTicket = requestedTicketCodes.FirstOrDefault(c => !ticketTypesByCode.ContainsKey(c));
        if (missingTicket is not null)
            throw new NotFoundException($"Ticket type '{missingTicket}' not found.");

        // 6. Resolve StationCode → TripStop
        var tripStopByStationCode = trip.TripStops
            .ToDictionary(
                ts => ts.RouteStop.Station.StationCode.ToUpperInvariant(),
                ts => ts);

        // 7. Validate từng item: from/to station hợp lệ và đúng thứ tự
        var resolvedItems = new List<ResolvedItem>();
        foreach (var item in request.Items)
        {
            var fromCode = item.FromStationCode.Trim().ToUpperInvariant();
            var toCode   = item.ToStationCode.Trim().ToUpperInvariant();

            if (!tripStopByStationCode.TryGetValue(fromCode, out var fromStop))
                throw new ValidationException([new ValidationFailure(nameof(item.FromStationCode),
                    $"Station '{fromCode}' is not a stop on this trip.")]);

            if (!tripStopByStationCode.TryGetValue(toCode, out var toStop))
                throw new ValidationException([new ValidationFailure(nameof(item.ToStationCode),
                    $"Station '{toCode}' is not a stop on this trip.")]);

            if (fromStop.StopOrder >= toStop.StopOrder)
                throw new ValidationException([new ValidationFailure(nameof(item.FromStationCode),
                    $"Station '{fromCode}' must come before '{toCode}' on the route.")]);

            resolvedItems.Add(new ResolvedItem(
                item,
                seatsByNumber[item.SeatNumber.Trim().ToUpperInvariant()],
                ticketTypesByCode[item.TicketTypeCode.Trim().ToUpperInvariant()],
                fromStop,
                toStop));
        }

        // 8. Validate & load promotion
        Promotion? promotion = null;
        if (!string.IsNullOrWhiteSpace(request.PromotionCode))
        {
            var promoCode = request.PromotionCode.Trim().ToUpperInvariant();
            promotion = await _context.Set<Promotion>()
                .SingleOrDefaultAsync(p => p.PromotionCode == promoCode, cancellationToken)
                ?? throw new ValidationException([new ValidationFailure(nameof(request.PromotionCode),
                    "Promotion code not found.")]);

            if (promotion.Status != "Active" || promotion.ValidFrom > now || promotion.ValidTo < now
                || (promotion.UsageLimit.HasValue && promotion.UsageCount >= promotion.UsageLimit))
                throw new ValidationException([new ValidationFailure(nameof(request.PromotionCode),
                    "Promotion code is not applicable.")]);
        }

        // 9. Tính unit_price qua FareCalculator (dùng internal GUID)
        var itemPrices = new List<(ResolvedItem Resolved, decimal UnitPrice)>();
        foreach (var resolved in resolvedItems)
        {
            var unitPrice = await _fareCalculator.CalculateAsync(
                trip.RouteId,
                resolved.FromStop.RouteStop.StationId,
                resolved.ToStop.RouteStop.StationId,
                resolved.TicketType.Id,
                cancellationToken);

            itemPrices.Add((resolved, unitPrice));
        }

        var subtotal = itemPrices.Sum(x => x.UnitPrice);

        // 10. Tính discount
        decimal discount = 0;
        if (promotion is not null)
        {
            discount = promotion.PromotionType == PromotionType.Percent
                ? subtotal * promotion.DiscountValue / 100
                : Math.Min(promotion.DiscountValue, subtotal);

            if (promotion.MinOrderValue.HasValue && subtotal < promotion.MinOrderValue)
                throw new ValidationException([new ValidationFailure(nameof(request.PromotionCode),
                    $"Minimum order value is {promotion.MinOrderValue:N0}.")]);
        }

        var total = subtotal - discount;

        // 11. Persist trong transaction
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        var seatHolds = itemPrices.Select(x => new SeatHold
        {
            TripId = trip.Id,
            SeatId = x.Resolved.Seat.Id,
            UserId = userId,
            HeldAt = now,
            ExpiresAt = now.AddMinutes(10),
            HoldStatus = SeatHoldStatus.Active
        }).ToList();
        _context.Set<SeatHold>().AddRange(seatHolds);

        var booking = new Booking
        {
            UserId = userId,
            PromotionId = promotion?.Id,
            BookingCode = await _bookingCodeGenerator.GenerateAsync(cancellationToken),
            BookedAt = now,
            BookingStatus = BookingStatus.PendingPayment,
            SubtotalAmount = subtotal,
            DiscountAmount = discount,
            TotalAmount = total
        };

        booking.Items = itemPrices.Select(x => new BookingItem
        {
            BookingId = booking.Id,
            TripId = trip.Id,
            TicketTypeId = x.Resolved.TicketType.Id,
            SeatId = x.Resolved.Seat.Id,
            FromTripStopId = x.Resolved.FromStop.Id,
            ToTripStopId = x.Resolved.ToStop.Id,
            PassengerName = x.Resolved.Item.PassengerName.Trim(),
            PassengerPhone = x.Resolved.Item.PassengerPhone?.Trim(),
            UnitPrice = x.UnitPrice,
            ItemStatus = BookingItemStatus.Active
        }).ToList();

        _context.Set<Booking>().Add(booking);

        if (promotion is not null)
            promotion.UsageCount++;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Items),
                "Booking failed due to a seat conflict. Please try again.")]);
        }

        return new CreateBookingResult(
            booking.Id, booking.BookingCode,
            booking.SubtotalAmount, booking.DiscountAmount, booking.TotalAmount,
            booking.BookingStatus.ToString(), booking.Items.Count);
    }

    private sealed record ResolvedItem(
        BookingItemRequest Item,
        Seat Seat,
        TicketType TicketType,
        TripStop FromStop,
        TripStop ToStop);
}
