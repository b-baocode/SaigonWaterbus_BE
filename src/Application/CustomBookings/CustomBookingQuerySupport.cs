using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CustomBookings;

internal static class CustomBookingQuerySupport
{
    public static IQueryable<Booking> BuildBaseQuery(IApplicationDbContext context) =>
        context.Set<Booking>()
            .Where(b => b.BookingType == Booking.CustomBookingType);

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

    public static CustomBookingDetailDto ToDetailDto(
        Booking booking,
        IReadOnlyCollection<Route>? relatedRoutes = null)
    {
        var routeEstimate = CustomBookingRoutePricingSupport.EstimateRoute(booking, relatedRoutes);

        var ticketDtos = CustomBookingTicketSupport.GetDisplayTickets(booking.Tickets)
            .Select(CustomBookingTicketSupport.ToDto)
            .ToList();

        return new CustomBookingDetailDto(
            booking.Id,
            booking.BookingCode,
            booking.Created,
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.Boat?.Name,
            booking.PassengerCount.GetValueOrDefault(),
            booking.AdultCount.GetValueOrDefault(),
            booking.ChildCount.GetValueOrDefault(),
            booking.PreferredNumberOfDecks,
            booking.PreferredSeatSetupType?.ToString(),
            booking.DepartureDate.GetValueOrDefault(),
            booking.StartTime,
            booking.RentalUnit.GetValueOrDefault().ToString(),
            booking.DurationValue.GetValueOrDefault(),
            CustomBookingRoutePricingSupport.ToDto(
                routeEstimate,
                booking.RentalUnit.GetValueOrDefault(),
                booking.DurationValue.GetValueOrDefault()),
            booking.FromStation?.StationName,
            booking.ToStation?.StationName,
            booking.ItineraryStops
                .OrderBy(x => x.StopOrder)
                .Select(x => new CustomBookingItineraryStopDto(
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
                .Select(CustomBookingPassengerSupport.ToDto)
                .ToList(),
            booking.Payments
                .OrderByDescending(x => x.Created)
                .Select(x => new CustomBookingPaymentDto(
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
