using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Routes;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingQuerySupport
{
    public static IQueryable<Booking> BuildBaseQuery(IApplicationDbContext context) =>
        context.Set<Booking>()
            .Where(b => b.BookingType == Booking.CharterBookingType);

    public static IQueryable<Booking> BuildDetailQuery(IApplicationDbContext context) =>
        BuildBaseQuery(context)
            .Include(b => b.Boat)
            .Include(b => b.CharterRoute)
            .Include(b => b.CharterBoats)
                .ThenInclude(cb => cb.Boat)
            .Include(b => b.AssignedManager)
            .Include(b => b.FromStation)
            .Include(b => b.ToStation)
            .Include(b => b.Promotion)
            .Include(b => b.ItineraryStops)
                .ThenInclude(s => s.Station)
            .Include(b => b.Passengers)
            .Include(b => b.Payments)
            .Include(b => b.Tickets)
                .ThenInclude(t => t.BookingPassenger);

    public static CharterBookingDetailDto ToDetailDto(
        Booking booking,
        IReadOnlyCollection<Route>? relatedRoutes = null)
    {
        var routeEstimate = CharterBookingRoutePricingSupport.EstimateRoute(booking, relatedRoutes);
        var rentalUnitForEstimate = CharterBookingRoutePricingSupport.ResolveRentalUnit(booking);
        var requestedDurationValueForEstimate = CharterBookingRoutePricingSupport.ResolveRequestedDurationValue(booking);
        var selectedBoatDtos = CharterBookingBoatSelectionSupport.ToSelectedBoatDtos(booking.CharterBoats);
        var selectedChargeableDurationValue = selectedBoatDtos.FirstOrDefault()?.ChargeableDurationValue;
        var selectedChargeableDurationMinutes =
            selectedChargeableDurationValue.HasValue && rentalUnitForEstimate == Domain.Enums.BoatRentalUnit.Hour
                ? decimal.Ceiling(selectedChargeableDurationValue.Value * 60m)
                : (decimal?)null;
        var hasSelectedQuotePricing = selectedChargeableDurationValue.HasValue;

        var ticketDtos = CharterBookingTicketSupport.GetDisplayTickets(booking.Tickets)
            .Select(CharterBookingTicketSupport.ToDto)
            .ToList();
        var requestedBoatDtos = CharterBookingBoatSelectionSupport.ToDtos(
            booking.RequestedBoatDecks,
            booking.RequestedBoatTypes);
        if (requestedBoatDtos.Count == 0 && booking.PreferredSeatSetupType.HasValue)
        {
            requestedBoatDtos = CharterBookingBoatSelectionSupport.ToLegacySeatSetupDtos([booking.PreferredSeatSetupType.Value]);
        }
        var requestedBoatCount = booking.RequestedBoatCount ?? requestedBoatDtos.Count;

        return new CharterBookingDetailDto(
            booking.Id,
            booking.Id,
            booking.BookingCode,
            booking.CharterBookingQrToken,
            booking.Created,
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.HoldExpiresAt,
            booking.Boat?.Name,
            booking.PassengerCount.GetValueOrDefault(),
            booking.AdultCount.GetValueOrDefault(),
            booking.ChildCount.GetValueOrDefault(),
            requestedBoatCount,
            requestedBoatDtos,
            selectedBoatDtos,
            booking.PreferredSeatSetupType?.ToString(),
            booking.DepartureDate.GetValueOrDefault(),
            booking.StartTime,
            booking.RentalUnit?.ToString(),
            booking.DurationValue,
            CharterBookingRoutePricingSupport.ToDto(
                routeEstimate,
                rentalUnitForEstimate,
                requestedDurationValueForEstimate,
                selectedChargeableDurationValue,
                selectedChargeableDurationMinutes,
                includeChargeablePricing: hasSelectedQuotePricing),
            booking.FromStationId,
            booking.ToStationId,
            booking.FromStation?.StationName,
            booking.ToStation?.StationName,
            booking.ItineraryStops
                .OrderBy(x => x.StopOrder)
                .Select(x => new CharterBookingItineraryStopDto(
                    x.StationId,
                    x.Station.StationName,
                    x.StopOrder,
                    x.StayDurationMinutes,
                    x.Note))
                .ToList(),
            booking.SpecialRequests,
            booking.SubtotalAmount,
            booking.DiscountAmount,
            booking.Promotion?.PromotionCode,
            booking.TotalAmount,
            booking.DepositAmount,
            booking.RemainingAmount,
            booking.RemainingAmount > 0,
            booking.ContactName,
            booking.ContactPhone,
            booking.ContactEmail,
            booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(CharterBookingPassengerSupport.ToDto)
                .ToList(),
            booking.Payments
                .OrderByDescending(x => x.Created)
                .Select(x => new CharterBookingPaymentDto(
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
            ticketDtos.Count,
            ticketDtos,
            CharterBookingAssignmentSupport.ToUserAssignmentDto(booking.AssignedManager),
            CharterBookingInsuranceSupport.ToDto(booking.InsuranceSnapshot),
            booking.InsuranceSnapshot is not null,
            booking.InsuranceSnapshot?.InsurancePackageId,
            booking.CharterRoute is null
                ? null
                : new CharterBookingSelectedRouteDto(
                    booking.CharterRoute.Id,
                    booking.CharterRoute.RouteCode,
                    booking.CharterRoute.RouteName,
                    booking.CharterRoute.RouteType,
                    RoutePresentationSupport.ResolveLabel(booking.CharterRoute.RouteType),
                    RoutePresentationSupport.IsGeneratedForBooking(booking.CharterRoute)),
            booking.BoatId);
    }
}
