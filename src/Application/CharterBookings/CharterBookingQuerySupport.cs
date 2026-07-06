using SaigonWaterbus.Application.Common.Interfaces;
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

        var ticketDtos = CharterBookingTicketSupport.GetDisplayTickets(booking.Tickets)
            .Select(CharterBookingTicketSupport.ToDto)
            .ToList();
        var requestedBoatDtos = CharterBookingBoatSelectionSupport.ToDtos(booking.RequestedBoatTypes);
        if (requestedBoatDtos.Count == 0 && booking.PreferredSeatSetupType.HasValue)
        {
            requestedBoatDtos = CharterBookingBoatSelectionSupport.ToDtos([booking.PreferredSeatSetupType.Value]);
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
            booking.Boat?.Name,
            booking.PassengerCount.GetValueOrDefault(),
            booking.AdultCount.GetValueOrDefault(),
            booking.ChildCount.GetValueOrDefault(),
            requestedBoatCount,
            requestedBoatDtos,
            booking.PreferredSeatSetupType?.ToString(),
            booking.DepartureDate.GetValueOrDefault(),
            booking.StartTime,
            booking.RentalUnit.GetValueOrDefault().ToString(),
            booking.DurationValue.GetValueOrDefault(),
            CharterBookingRoutePricingSupport.ToDto(
                routeEstimate,
                booking.RentalUnit.GetValueOrDefault(),
                booking.DurationValue.GetValueOrDefault()),
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
            booking.BoatRequirements,
            booking.SpecialRequests,
            booking.SubtotalAmount,
            booking.DiscountAmount,
            booking.Promotion?.PromotionCode,
            booking.TotalAmount,
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
                    x.RefundAmount,
                    x.RefundReferenceId,
                    x.RefundPayoutId,
                    x.RefundStatus,
                    x.RefundFailureReason,
                    x.RefundedAt))
                .ToList(),
            ticketDtos.Count,
            ticketDtos);
    }
}
