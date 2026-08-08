using FluentValidation.Results;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

/// <summary>
/// Validates a regular/sightseeing booking and calculates the current server-side
/// price without creating a booking or holding seats.
/// </summary>
public sealed record PreviewBookingCommand(
    string TripCode,
    IReadOnlyList<BookingItemRequest> Items,
    string? PromotionCode = null,
    string? ReturnTripCode = null,
    IReadOnlyList<BookingItemRequest>? ReturnItems = null,
    bool? InsuranceSelected = null,
    Guid? InsurancePackageId = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null) : IRequest<PreviewBookingResult>;

public sealed record PreviewBookingLineItem(
    string TicketTypeCode,
    string? SeatNumber,
    string PassengerName,
    decimal UnitPrice);

public sealed record PreviewBookingResult(
    Guid TripId,
    string TripCode,
    Guid? ReturnTripId,
    string? ReturnTripCode,
    decimal TicketSubtotalAmount,
    decimal InsuranceAmount,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    DateTimeOffset HoldExpiresAt,
    IReadOnlyList<PreviewBookingLineItem> Items,
    IReadOnlyList<PreviewBookingLineItem> ReturnItems,
    BookingInsuranceDto? Insurance,
    bool PromotionValid,
    string? PromotionMessage);

public sealed class PreviewBookingCommandHandler
    : IRequestHandler<PreviewBookingCommand, PreviewBookingResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IFareCalculator _fareCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly ISeatHoldService _seatHoldService;

    public PreviewBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IFareCalculator fareCalculator,
        TimeProvider timeProvider,
        ISeatHoldService? seatHoldService = null)
    {
        _context = context;
        _userContext = userContext;
        _fareCalculator = fareCalculator;
        _timeProvider = timeProvider;
        _seatHoldService = seatHoldService ?? NullSeatHoldService.Instance;
    }

    public async Task<PreviewBookingResult> Handle(
        PreviewBookingCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var validation = await new CreateBookingCommandValidator().ValidateAsync(
            new CreateBookingCommand(
                request.TripCode,
                request.Items,
                request.PromotionCode,
                request.ReturnTripCode,
                request.ReturnItems,
                request.InsuranceSelected,
                request.InsurancePackageId,
                request.ContactName,
                request.ContactPhone,
                request.ContactEmail),
            cancellationToken);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        var now = _timeProvider.GetUtcNow();
        var resolver = new BookingLegResolver(_context, _fareCalculator, _seatHoldService);
        var outboundLeg = await resolver.ResolveAsync(
            request.TripCode,
            request.Items,
            userId,
            now,
            nameof(request.TripCode),
            allowDepartedTrip: false,
            cancellationToken);

        ResolvedLeg? returnLeg = null;
        if (!string.IsNullOrWhiteSpace(request.ReturnTripCode))
        {
            returnLeg = await resolver.ResolveAsync(
                request.ReturnTripCode,
                request.ReturnItems!,
                userId,
                now,
                nameof(request.ReturnTripCode),
                allowDepartedTrip: false,
                cancellationToken);

            BookingLegResolver.EnsureLegsDoNotShareSeats(outboundLeg, returnLeg);
            BookingLegResolver.EnsureReturnLegStartsAfterOutboundLeg(
                outboundLeg,
                returnLeg,
                nameof(request.ReturnTripCode));
        }

        var legs = returnLeg is null
            ? new[] { outboundLeg }
            : new[] { outboundLeg, returnLeg };
        var ticketSubtotal = legs.Sum(leg => leg.ItemPrices.Sum(x => x.UnitPrice));
        var insurance = await CharterBookingInsuranceSupport.ResolveSeatBookingInsuranceSnapshotAsync(
            _context,
            request.InsuranceSelected,
            request.InsurancePackageId,
            legs.Sum(x => x.ItemPrices.Count),
            now,
            cancellationToken);
        var subtotal = PriceRoundingSupport.RoundFare(ticketSubtotal + (insurance?.TotalAmount ?? 0m));

        var discount = 0m;
        var promotionValid = string.IsNullOrWhiteSpace(request.PromotionCode);
        string? promotionMessage = null;
        if (!string.IsNullOrWhiteSpace(request.PromotionCode))
        {
            var code = PromotionSupport.NormalizeCode(request.PromotionCode);
            var promotion = await _context.Set<Promotion>()
                .SingleOrDefaultAsync(x => x.PromotionCode == code, cancellationToken);
            if (promotion is null)
            {
                promotionMessage = "Không tìm thấy mã khuyến mãi.";
            }
            else
            {
                var applyContext = new PromotionApplyContext(
                    Booking.SeatBookingType,
                    outboundLeg.Trip.RouteId,
                    outboundLeg.Trip.OperatingDate.DayOfWeek,
                    TimeOnly.FromTimeSpan(outboundLeg.Trip.DepartureTime.TimeOfDay));
                var eligibility = await PromotionEligibilitySupport.EvaluateAsync(
                    _context,
                    promotion,
                    userId,
                    subtotal,
                    now,
                    applyContext,
                    excludedBookingId: null,
                    cancellationToken);
                promotionValid = eligibility.IsValid;
                discount = eligibility.Discount;
                promotionMessage = eligibility.IsValid
                    ? "Áp dụng khuyến mãi thành công."
                    : eligibility.Reason;
            }
        }

        var total = PriceRoundingSupport.RoundFare(subtotal - discount);
        return new PreviewBookingResult(
            outboundLeg.Trip.Id,
            outboundLeg.Trip.TripCode,
            returnLeg?.Trip.Id,
            returnLeg?.Trip.TripCode,
            ticketSubtotal,
            insurance?.TotalAmount ?? 0m,
            subtotal,
            discount,
            total,
            BookingLegResolver.ResolveHoldExpiresAt(legs, now),
            ToLineItems(outboundLeg),
            returnLeg is null ? [] : ToLineItems(returnLeg),
            BookingInsuranceDtoMapper.ToDto(insurance),
            promotionValid,
            promotionMessage);
    }

    private static IReadOnlyList<PreviewBookingLineItem> ToLineItems(ResolvedLeg leg) =>
        leg.ItemPrices
            .Select(x => new PreviewBookingLineItem(
                x.Resolved.TicketType.Code,
                x.Resolved.Item.SeatNumber,
                x.Resolved.Item.PassengerName,
                x.UnitPrice))
            .ToArray();
}
