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
        var depositPlan = CharterBookingPaymentSupport.ComputeDepositPlan(booking);
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
            booking.DepartureDate,
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
            // Tiền tàu thuần (không gồm bảo hiểm) = tổng SubtotalAmount của các CharterBookingBoat đã chọn.
            booking.CharterBoats?.Where(x => x.BoatOrder > 0).Sum(x => x.SubtotalAmount) ?? 0m,
            booking.GetTotalInsuranceAmount(),
            booking.DiscountAmount,
            booking.Promotion?.PromotionCode,
            booking.TotalAmount,
            booking.DepositAmount,
            booking.RemainingAmount,
            // BE tính sẵn để FE không phải đoán — đảm bảo nút "Đặt cọc / Thanh toán đủ / Phần còn lại" enabled đúng.
            RequiresAdditionalPayment: booking.RemainingAmount > 0,
            ContactName: booking.ContactName,
            ContactPhone: booking.ContactPhone,
            ContactEmail: booking.ContactEmail,
            Passengers: booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(CharterBookingPassengerSupport.ToDto)
                .ToList(),
            Payments: booking.Payments
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
            TicketCount: ticketDtos.Count,
            Tickets: ticketDtos,
            SuggestedDepositAmount: depositPlan.SuggestedDepositAmount,
            HasDepositPaid: depositPlan.HasDepositPaid,
            AssignedManager: CharterBookingAssignmentSupport.ToUserAssignmentDto(booking.AssignedManager),
            Insurance: CharterBookingInsuranceSupport.ToDto(booking.GetDefaultInsurance()),
            InsuranceSelected: (booking.InsuranceSnapshots ?? new List<SaigonWaterbus.Domain.Entities.BookingInsuranceSnapshot>()).Count > 0,
            InsurancePackageId: booking.GetDefaultInsurance()?.InsurancePackageId,
            OptionalInsurances: booking.HasOptionalInsurance()
                ? CharterBookingInsuranceSupport.ToDtos(booking.GetOptionalInsurances())
                : null,
            SelectedRoute: booking.CharterRoute is null
                ? null
                : new CharterBookingSelectedRouteDto(
                    booking.CharterRoute.Id,
                    booking.CharterRoute.RouteCode,
                    booking.CharterRoute.RouteName,
                    booking.CharterRoute.RouteType,
                    RoutePresentationSupport.ResolveLabel(booking.CharterRoute.RouteType),
                    RoutePresentationSupport.IsGeneratedForBooking(booking.CharterRoute)),
            BoatId: booking.BoatId,
            RefundSummary: ToRefundSummaryDto(booking, DateTimeOffset.UtcNow),
            RefundablePayments: ToRefundablePaymentDtos(booking, DateTimeOffset.UtcNow));
    }

    private static CharterBookingRefundSummaryDto? ToRefundSummaryDto(Booking booking, DateTimeOffset now)
    {
        var summary = CharterBookingRefundSupport.BuildSummary(booking, now);
        var isPartiallyRefunded = summary.OutstandingRefundAmount > 0m
            && summary.TotalRefundedAmount > 0m
            && summary.TotalPaidAmount > 0m;
        var isFullyRefunded = summary.OutstandingRefundAmount == 0m && summary.TotalPaidAmount > 0m;
        return new CharterBookingRefundSummaryDto(
            summary.TotalPaidAmount,
            summary.TotalRefundedAmount,
            summary.OutstandingRefundAmount,
            summary.PolicyPercent,
            summary.TimeUntilDeparture?.TotalHours,
            summary.CanRequestRefund,
            summary.PolicyMessage,
            isPartiallyRefunded,
            isFullyRefunded);
    }

    private static IReadOnlyList<CharterBookingRefundablePaymentDto> ToRefundablePaymentDtos(
        Booking booking,
        DateTimeOffset now)
    {
        var summary = CharterBookingRefundSupport.BuildSummary(booking, now);
        return CharterBookingRefundSupport
            .GetRefundablePayments(booking, summary)
            .Select(x => new CharterBookingRefundablePaymentDto(
                x.PaymentId,
                x.PaymentCode,
                x.PaidAmount,
                x.AlreadyRefundedAmount,
                x.AvailableRefundAmount,
                x.PaymentStatus))
            .ToList();
    }
}
