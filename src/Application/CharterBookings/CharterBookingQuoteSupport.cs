using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal sealed record QuoteBoatSelection(
    int BoatOrder,
    Guid BoatId,
    Boat Boat);

internal sealed record PricedQuoteBoat(
    int BoatOrder,
    Boat Boat,
    CharterBookingRoutePricingEstimate Pricing);

internal static class CharterBookingQuoteSupport
{
    private const string PendingPaymentStatus = "Pending";
    private const string PaidPaymentStatus = "Paid";

    public static IReadOnlyList<QuoteCharterBookingBoatRequest> ResolveQuoteBoatRequests(
        Guid? boatId,
        IReadOnlyList<QuoteCharterBookingBoatRequest>? boats)
    {
        if (boats is { Count: > 0 })
        {
            return boats
                .OrderBy(x => x.BoatOrder)
                .ToArray();
        }

        return boatId.HasValue
            ? [new QuoteCharterBookingBoatRequest(1, boatId.Value)]
            : [];
    }

    public static async Task<IReadOnlyList<QuoteBoatSelection>> LoadSelectedBoatsAsync(
        IApplicationDbContext context,
        Guid? boatId,
        IReadOnlyList<QuoteCharterBookingBoatRequest>? boats,
        CancellationToken cancellationToken)
    {
        var quoteBoatRequests = ResolveQuoteBoatRequests(boatId, boats);
        var selectedBoatIds = quoteBoatRequests
            .Select(x => x.BoatId)
            .Distinct()
            .ToArray();
        var loadedBoats = await context.Set<Boat>()
            .Where(x => selectedBoatIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (loadedBoats.Count != selectedBoatIds.Length)
        {
            throw new NotFoundException("Boat not found.");
        }

        var boatsById = loadedBoats.ToDictionary(x => x.Id);
        return quoteBoatRequests
            .OrderBy(x => x.BoatOrder)
            .Select(x => new QuoteBoatSelection(x.BoatOrder, x.BoatId, boatsById[x.BoatId]))
            .ToArray();
    }

    public static IReadOnlyList<Guid> GetSelectedBoatIds(IReadOnlyList<QuoteBoatSelection> selectedBoats) =>
        selectedBoats
            .Select(x => x.BoatId)
            .Distinct()
            .ToArray();

    public static void ValidateSelectedBoats(
        Booking booking,
        IReadOnlyList<QuoteBoatSelection> selectedBoats)
    {
        if (selectedBoats.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("Boats",
                "Cần chọn ít nhất một tàu để chốt giá.")]);
        }

        if (selectedBoats.Any(x => x.BoatOrder <= 0))
        {
            throw new ValidationException([new ValidationFailure("Boats",
                "boatOrder phải lớn hơn 0.")]);
        }

        if (selectedBoats.Select(x => x.BoatOrder).Distinct().Count() != selectedBoats.Count)
        {
            throw new ValidationException([new ValidationFailure("Boats",
                "boatOrder không được trùng.")]);
        }

        if (selectedBoats.Select(x => x.BoatId).Distinct().Count() != selectedBoats.Count)
        {
            throw new ValidationException([new ValidationFailure("Boats",
                "boatId không được trùng trong cùng một quote.")]);
        }

        var requestedBoatDecks = ResolveRequestedBoatDecks(booking);
        var requestedBoatTypes = ResolveRequestedBoatTypes(booking);
        var requestedBoatCount = ResolveRequestedBoatCount(booking, requestedBoatDecks, requestedBoatTypes);
        if (selectedBoats.Count != requestedBoatCount)
        {
            throw new ValidationException([new ValidationFailure("Boats",
                $"Số tàu admin chọn ({selectedBoats.Count}) phải bằng số tàu khách yêu cầu ({requestedBoatCount}).")]);
        }

        var selectedOrders = selectedBoats
            .Select(x => x.BoatOrder)
            .OrderBy(x => x)
            .ToArray();
        var expectedOrders = Enumerable.Range(1, requestedBoatCount).ToArray();
        if (!selectedOrders.SequenceEqual(expectedOrders))
        {
            throw new ValidationException([new ValidationFailure("Boats",
                $"boatOrder phải đủ từ 1 đến {requestedBoatCount}.")]);
        }

        foreach (var selectedBoat in selectedBoats)
        {
            if (selectedBoat.Boat.ServiceType != BoatServiceType.Passenger
                || selectedBoat.Boat.Status != BoatStatus.Active)
            {
                throw new ValidationException([new ValidationFailure("Boats",
                    $"Tàu thứ {selectedBoat.BoatOrder} hiện không khả dụng để thuê.")]);
            }

            if (selectedBoat.BoatOrder <= requestedBoatDecks.Count)
            {
                var requestedNumberOfDecks = requestedBoatDecks[selectedBoat.BoatOrder - 1];
                if (selectedBoat.Boat.NumberOfDecks != requestedNumberOfDecks)
                {
                    throw new ValidationException([new ValidationFailure("Boats",
                        $"Tàu thứ {selectedBoat.BoatOrder} có {selectedBoat.Boat.NumberOfDecks} tầng, không trùng số tầng khách yêu cầu ({requestedNumberOfDecks}).")]);
                }
            }
            else if (selectedBoat.BoatOrder <= requestedBoatTypes.Count)
            {
                var requestedSeatSetupType = requestedBoatTypes[selectedBoat.BoatOrder - 1];
                if (selectedBoat.Boat.SeatSetupType != requestedSeatSetupType)
                {
                    throw new ValidationException([new ValidationFailure("Boats",
                        $"Tàu thứ {selectedBoat.BoatOrder} là {selectedBoat.Boat.SeatSetupType}, không trùng kiểu khách yêu cầu ({requestedSeatSetupType}).")]);
                }
            }
        }

        var passengerCount = booking.PassengerCount.GetValueOrDefault();
        var totalSeatCount = selectedBoats.Sum(x => x.Boat.SeatCount);
        if (passengerCount > totalSeatCount)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PassengerCount),
                $"Số khách vượt quá tổng sức chứa của các tàu được chọn ({totalSeatCount}).")]);
        }
    }

    public static IReadOnlyList<PricedQuoteBoat> EstimateSelectedBoatPrices(
        Booking booking,
        IReadOnlyList<QuoteBoatSelection> selectedBoats,
        BoatRentalUnit rentalUnit,
        int requestedDurationValue,
        IReadOnlyCollection<Route>? relatedRoutes) =>
        selectedBoats
            .Select(x => new PricedQuoteBoat(
                x.BoatOrder,
                x.Boat,
                CharterBookingRoutePricingSupport.EstimatePrice(
                    booking,
                    x.Boat,
                    rentalUnit,
                    requestedDurationValue,
                    relatedRoutes)))
            .ToArray();

    public static IReadOnlyList<CharterBookingSelectedBoatDto> ToSelectedBoatDtos(
        IEnumerable<PricedQuoteBoat> selectedBoatPricings) =>
        selectedBoatPricings
            .OrderBy(x => x.BoatOrder)
            .Select(x => new CharterBookingSelectedBoatDto(
                x.BoatOrder,
                x.Boat.Id,
                x.Boat.Name,
                x.Boat.SeatSetupType.ToString(),
                x.Boat.NumberOfDecks,
                x.Pricing.UnitPrice,
                x.Pricing.ChargeableDurationValue,
                x.Pricing.SubtotalAmount))
            .ToArray();

    public static async Task<Promotion?> ResolvePromotionForQuoteAsync(
        IApplicationDbContext context,
        Booking booking,
        string? promotionCode,
        decimal subtotalAmount,
        DateTimeOffset now,
        string promotionCodeFieldName,
        CancellationToken cancellationToken)
    {
        // Loại trừ chính booking đang xét khỏi việc đếm lượt (re-quote không tự chặn mình).
        if (promotionCode is not null)
        {
            return string.IsNullOrWhiteSpace(promotionCode)
                ? null
                : await CharterBookingPricingSupport.ResolvePromotionAsync(
                    context,
                    promotionCode,
                    booking.UserId,
                    subtotalAmount,
                    now,
                    promotionCodeFieldName,
                    booking.Id,
                    cancellationToken);
        }

        if (!booking.PromotionId.HasValue)
        {
            return null;
        }

        var promotion = booking.Promotion
            ?? await context.Set<Promotion>()
                .SingleOrDefaultAsync(x => x.Id == booking.PromotionId.Value, cancellationToken);
        if (promotion is null)
        {
            return null;
        }

        await CharterBookingPricingSupport.EnsurePromotionCanBeUsedAsync(
            context,
            promotion,
            booking.UserId,
            subtotalAmount,
            now,
            promotionCodeFieldName,
            booking.Id,
            cancellationToken);
        return promotion;
    }

    public static void EnsureCanQuote(Booking booking)
    {
        if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Completed
            or BookingStatus.Refunded or BookingStatus.Confirmed)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể chốt giá cho booking đã hủy, đã xác nhận, đã hoàn tất hoặc đã hoàn tiền.")]);
        }

        if (booking.Payments.Any(x =>
                string.Equals(x.PaymentStatus, PendingPaymentStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.PaymentStatus, PaidPaymentStatus, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.Payments),
                "Booking đã có payment đang chờ hoặc đã thanh toán nên không thể đổi tàu/giá.")]);
        }
    }

    public static async Task ReleaseQuoteHoldsAsync(
        IBoatHoldService boatHoldService,
        Guid bookingId,
        IEnumerable<Guid> boatIds,
        DateOnly departureDate,
        TimeOnly? startTime,
        BoatRentalUnit rentalUnit,
        int durationValue,
        CancellationToken cancellationToken)
    {
        foreach (var boatId in boatIds.Distinct())
        {
            await boatHoldService.ReleaseAsync(
                bookingId,
                boatId,
                departureDate,
                startTime,
                rentalUnit,
                durationValue,
                cancellationToken);
        }
    }

    private static IReadOnlyList<int> ResolveRequestedBoatDecks(Booking booking) =>
        CharterBookingBoatSelectionSupport.FromDeckStorageValue(booking.RequestedBoatDecks);

    private static IReadOnlyList<SeatSetupType> ResolveRequestedBoatTypes(Booking booking)
    {
        var requestedBoatTypes = CharterBookingBoatSelectionSupport.FromSeatSetupStorageValue(booking.RequestedBoatTypes);
        if (requestedBoatTypes.Count == 0 && booking.PreferredSeatSetupType.HasValue)
        {
            requestedBoatTypes = [booking.PreferredSeatSetupType.Value];
        }

        return requestedBoatTypes;
    }

    private static int ResolveRequestedBoatCount(
        Booking booking,
        IReadOnlyList<int> requestedBoatDecks,
        IReadOnlyList<SeatSetupType> requestedBoatTypes) =>
        booking.RequestedBoatCount.GetValueOrDefault() > 0
            ? booking.RequestedBoatCount!.Value
            : Math.Max(1, Math.Max(requestedBoatDecks.Count, requestedBoatTypes.Count));
}
