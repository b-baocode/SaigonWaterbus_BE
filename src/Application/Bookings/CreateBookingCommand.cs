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
    Guid SeatId,
    Guid TicketTypeId,
    Guid FromTripStopId,
    Guid ToTripStopId,
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
    Guid TripId,
    IReadOnlyList<BookingItemRequest> Items,
    string? PromotionCode) : IRequest<CreateBookingResult>;

// ─── Validator ───────────────────────────────────────────
public sealed class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty().WithMessage("Booking must have at least one item.")
            .Must(items => items.Count <= 10).WithMessage("Maximum 10 items per booking.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.SeatId).NotEmpty();
            item.RuleFor(x => x.TicketTypeId).NotEmpty();
            item.RuleFor(x => x.FromTripStopId).NotEmpty();
            item.RuleFor(x => x.ToTripStopId).NotEmpty();
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
        // 1. Load trip với trip_stops và route_stops
        var trip = await _context.Set<Trip>()
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.RouteStop)
            .SingleOrDefaultAsync(t => t.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        // 2. Validate trip còn bookable
        var now = _timeProvider.GetUtcNow();
        if (trip.TripStatus != TripStatus.Scheduled || trip.DepartureTime <= now)
            throw new ValidationException([new ValidationFailure(nameof(request.TripId), "Trip is not available for booking.")]);

        // 3. Validate seats available
        var requestedSeatIds = request.Items.Select(i => i.SeatId).ToList();
        var availableSeats = await _seatAvailability.GetAvailableSeatsAsync(request.TripId, requestedSeatIds, cancellationToken);
        var occupiedSeats = requestedSeatIds.Except(availableSeats).ToList();
        if (occupiedSeats.Count > 0)
            throw new ValidationException([new ValidationFailure(nameof(request.Items), "One or more seats are no longer available.")]);

        // 4. Validate trip_stops: from < to theo stop_order
        var stopOrders = trip.TripStops.ToDictionary(ts => ts.Id, ts => ts.StopOrder);
        foreach (var item in request.Items)
        {
            if (!stopOrders.TryGetValue(item.FromTripStopId, out var fromOrder) ||
                !stopOrders.TryGetValue(item.ToTripStopId, out var toOrder) ||
                fromOrder >= toOrder)
                throw new ValidationException([new ValidationFailure(nameof(request.Items), "Invalid boarding or alighting stop.")]);
        }

        // 5. Validate & load promotion
        Promotion? promotion = null;
        if (!string.IsNullOrWhiteSpace(request.PromotionCode))
        {
            var code = request.PromotionCode.Trim().ToUpperInvariant();
            promotion = await _context.Set<Promotion>()
                .SingleOrDefaultAsync(p => p.PromotionCode == code, cancellationToken)
                ?? throw new ValidationException([new ValidationFailure(nameof(request.PromotionCode), "Promotion code not found.")]);

            if (promotion.Status != "Active" || promotion.ValidFrom > now || promotion.ValidTo < now
                || (promotion.UsageLimit.HasValue && promotion.UsageCount >= promotion.UsageLimit))
                throw new ValidationException([new ValidationFailure(nameof(request.PromotionCode), "Promotion code is not applicable.")]);
        }

        // 6. Tính unit_price cho từng item qua FareCalculator
        var itemPrices = new List<(BookingItemRequest Item, decimal UnitPrice)>();
        foreach (var item in request.Items)
        {
            var fromStop = trip.TripStops.Single(ts => ts.Id == item.FromTripStopId);
            var toStop = trip.TripStops.Single(ts => ts.Id == item.ToTripStopId);

            var unitPrice = await _fareCalculator.CalculateAsync(
                trip.RouteId,
                fromStop.RouteStop.StationId,
                toStop.RouteStop.StationId,
                item.TicketTypeId,
                cancellationToken);

            itemPrices.Add((item, unitPrice));
        }

        var subtotal = itemPrices.Sum(x => x.UnitPrice);

        // 7. Tính discount
        decimal discount = 0;
        if (promotion is not null)
        {
            discount = promotion.PromotionType == PromotionType.Percent
                ? subtotal * promotion.DiscountValue / 100
                : Math.Min(promotion.DiscountValue, subtotal);

            if (promotion.MinOrderValue.HasValue && subtotal < promotion.MinOrderValue)
                throw new ValidationException([new ValidationFailure(nameof(request.PromotionCode), $"Minimum order value is {promotion.MinOrderValue:N0}.")]);
        }

        var total = subtotal - discount;

        // 8. Persist trong transaction
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        // SeatHolds
        var seatHolds = request.Items.Select(item => new SeatHold
        {
            TripId = request.TripId,
            SeatId = item.SeatId,
            UserId = userId,
            HeldAt = now,
            ExpiresAt = now.AddMinutes(10),
            HoldStatus = SeatHoldStatus.Active
        }).ToList();
        _context.Set<SeatHold>().AddRange(seatHolds);

        // Booking
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

        // BookingItems
        booking.Items = itemPrices.Select(x => new BookingItem
        {
            BookingId = booking.Id,
            TripId = request.TripId,
            TicketTypeId = x.Item.TicketTypeId,
            SeatId = x.Item.SeatId,
            FromTripStopId = x.Item.FromTripStopId,
            ToTripStopId = x.Item.ToTripStopId,
            PassengerName = x.Item.PassengerName.Trim(),
            PassengerPhone = x.Item.PassengerPhone?.Trim(),
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
            throw new ValidationException([new ValidationFailure(nameof(request.Items), "Booking failed due to a seat conflict. Please try again.")]);
        }

        return new CreateBookingResult(
            booking.Id, booking.BookingCode,
            booking.SubtotalAmount, booking.DiscountAmount, booking.TotalAmount,
            booking.BookingStatus.ToString(), booking.Items.Count);
    }
}
