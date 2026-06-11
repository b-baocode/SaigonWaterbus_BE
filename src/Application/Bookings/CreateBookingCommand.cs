using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

public sealed record BookingItemRequest(
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
            item.RuleFor(x => x.FromStationCode).NotEmpty().MaximumLength(50);
            item.RuleFor(x => x.ToStationCode).NotEmpty().MaximumLength(50)
                .NotEqual(x => x.FromStationCode).WithMessage("From and To stations must be different.");
            item.RuleFor(x => x.PassengerName).NotEmpty().MaximumLength(150);
            item.RuleFor(x => x.PassengerPhone).MaximumLength(20).When(x => x.PassengerPhone is not null);
        });
    }
}

public sealed class CreateBookingCommandHandler : IRequestHandler<CreateBookingCommand, CreateBookingResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBookingCodeGenerator _bookingCodeGenerator;
    private readonly IFareCalculator _fareCalculator;
    private readonly TimeProvider _timeProvider;

    public CreateBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBookingCodeGenerator bookingCodeGenerator,
        IFareCalculator fareCalculator,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _bookingCodeGenerator = bookingCodeGenerator;
        _fareCalculator = fareCalculator;
        _timeProvider = timeProvider;
    }

    public async Task<CreateBookingResult> Handle(CreateBookingCommand request, CancellationToken cancellationToken)
    {
        var tripCode = request.TripCode.Trim().ToUpperInvariant();

        var trip = await _context.Set<Trip>()
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.RouteStop)
                    .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(t => t.TripCode == tripCode, cancellationToken)
            ?? throw new NotFoundException($"Trip '{tripCode}' not found.");

        var now = _timeProvider.GetUtcNow();
        if (trip.TripStatus != TripStatus.Scheduled || trip.DepartureTime <= now)
            throw new ValidationException([new ValidationFailure(nameof(request.TripCode),
                "Trip is not available for booking.")]);

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

        var tripStopByStationCode = trip.TripStops
            .ToDictionary(
                ts => ts.RouteStop.Station.StationCode.ToUpperInvariant(),
                ts => ts);

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
                ticketTypesByCode[item.TicketTypeCode.Trim().ToUpperInvariant()],
                fromStop,
                toStop));
        }

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

        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

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
            await _context.ExecuteInTransactionAsync(
                async ct =>
                {
                    await _context.SaveChangesAsync(ct);
                },
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Items),
                "Booking failed. Please try again.")]);
        }

        return new CreateBookingResult(
            booking.Id, booking.BookingCode,
            booking.SubtotalAmount, booking.DiscountAmount, booking.TotalAmount,
            booking.BookingStatus.ToString(), booking.Items.Count);
    }

    private sealed record ResolvedItem(
        BookingItemRequest Item,
        TicketType TicketType,
        TripStop FromStop,
        TripStop ToStop);
}
