using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingPassengerResultSupport
{
    public static UpdateCharterBookingPassengersResult ToUpdateResult(
        Booking booking,
        IReadOnlyList<Ticket>? tickets = null,
        decimal additionalInsuranceAmount = 0m)
    {
        var displayTickets = tickets ?? CharterBookingTicketSupport.GetDisplayTickets(booking.Tickets);
        return new UpdateCharterBookingPassengersResult(
            booking.Id,
            booking.CharterBookingQrToken,
            booking.PassengerCount.GetValueOrDefault(),
            booking.Passengers.Count,
            CharterBookingPassengerSupport.CountAdults(booking.Passengers),
            CharterBookingPassengerSupport.CountChildren(booking.Passengers),
            booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(CharterBookingPassengerSupport.ToDto)
                .ToList(),
            displayTickets.Count,
            displayTickets.Select(CharterBookingTicketSupport.ToDto).ToList(),
            booking.PaymentStatus,
            booking.TotalAmount,
            booking.DepositAmount,
            booking.RemainingAmount,
            booking.RemainingAmount > 0,
            additionalInsuranceAmount,
            CharterBookingInsuranceSupport.ToDto(booking.InsuranceSnapshot));
    }

    public static ImportCharterBookingPassengersResult ToImportResult(
        Booking booking,
        IReadOnlyList<Ticket>? tickets = null,
        decimal additionalInsuranceAmount = 0m)
    {
        var displayTickets = tickets ?? CharterBookingTicketSupport.GetDisplayTickets(booking.Tickets);
        return new ImportCharterBookingPassengersResult(
            booking.Id,
            booking.CharterBookingQrToken,
            booking.PassengerCount.GetValueOrDefault(),
            booking.Passengers.Count,
            CharterBookingPassengerSupport.CountAdults(booking.Passengers),
            CharterBookingPassengerSupport.CountChildren(booking.Passengers),
            booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(CharterBookingPassengerSupport.ToDto)
                .ToList(),
            displayTickets.Count,
            displayTickets.Select(CharterBookingTicketSupport.ToDto).ToList(),
            booking.PaymentStatus,
            booking.TotalAmount,
            booking.DepositAmount,
            booking.RemainingAmount,
            booking.RemainingAmount > 0,
            additionalInsuranceAmount,
            CharterBookingInsuranceSupport.ToDto(booking.InsuranceSnapshot));
    }
}
