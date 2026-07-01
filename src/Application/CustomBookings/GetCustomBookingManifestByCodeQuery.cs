using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookings;

public sealed record GetCustomBookingManifestByCodeQuery(string BookingCode)
    : IRequest<CustomBookingManifestDto>;

public sealed class GetCustomBookingManifestByCodeQueryValidator
    : AbstractValidator<GetCustomBookingManifestByCodeQuery>
{
    public GetCustomBookingManifestByCodeQueryValidator()
    {
        RuleFor(x => x.BookingCode).NotEmpty().MaximumLength(50);
    }
}

public sealed class GetCustomBookingManifestByCodeQueryHandler
    : IRequestHandler<GetCustomBookingManifestByCodeQuery, CustomBookingManifestDto>
{
    private const string PaidBookingPaymentStatus = "Paid";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCustomBookingManifestByCodeQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CustomBookingManifestDto> Handle(
        GetCustomBookingManifestByCodeQuery request,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(
            _context,
            _userContext,
            cancellationToken);
        var normalizedBookingCode = request.BookingCode.Trim().ToUpperInvariant();

        var booking = await CustomBookingQuerySupport.BuildBaseQuery(_context)
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .Include(x => x.Passengers)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.CheckedInByUser)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.CheckedOutByUser)
            .SingleOrDefaultAsync(
                x => x.BookingCode.ToUpper() == normalizedBookingCode,
                cancellationToken)
            ?? throw new NotFoundException("Custom booking not found.");

        if (booking.UserId != currentUser.Id
            && !AuthSupport.IsAdmin(currentUser)
            && !AuthSupport.IsManager(currentUser)
            && !AuthSupport.IsStaff(currentUser))
        {
            throw new NotFoundException("Custom booking not found.");
        }

        var currentTickets = CustomBookingTicketSupport.GetDisplayTickets(booking.Tickets);
        var ticketsByPassengerId = currentTickets
            .Where(x => x.BookingPassengerId.HasValue)
            .ToDictionary(x => x.BookingPassengerId!.Value);
        var canCheckInBooking = booking.BookingStatus == BookingStatus.Confirmed
            && (string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase)
                || booking.RemainingAmount <= 0);

        return new CustomBookingManifestDto(
            booking.Id,
            booking.BookingCode,
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.ContactName,
            booking.ContactPhone,
            booking.ContactEmail,
            booking.DepartureDate,
            booking.StartTime,
            booking.Boat?.Name,
            booking.FromStation?.StationName,
            booking.ToStation?.StationName,
            ToItineraryStopDtos(booking),
            booking.PassengerCount.GetValueOrDefault(),
            booking.Passengers.Count,
            CustomBookingPassengerSupport.CountAdults(booking.Passengers),
            CustomBookingPassengerSupport.CountChildren(booking.Passengers),
            new CustomBookingTicketSummaryDto(
                currentTickets.Count,
                currentTickets.Count(x => x.TicketStatus == TicketStatus.Active),
                currentTickets.Count(x => x.TicketStatus == TicketStatus.CheckedIn),
                currentTickets.Count(x => x.TicketStatus == TicketStatus.CheckedOut)),
            booking.Passengers
                .OrderBy(x => x.FullName)
                .ThenBy(x => x.Id)
                .Select(passenger =>
                {
                    ticketsByPassengerId.TryGetValue(passenger.Id, out var ticket);
                    return ToPassengerDto(passenger, ticket, canCheckInBooking);
                })
                .ToList());
    }

    internal static IReadOnlyList<CustomBookingItineraryStopDto> ToItineraryStopDtos(Booking booking) =>
        booking.ItineraryStops
            .OrderBy(x => x.StopOrder)
            .Select(x => new CustomBookingItineraryStopDto(
                x.StationId,
                x.Station.StationName,
                x.StopOrder,
                x.StayDurationMinutes,
                x.Note))
            .ToList();

    private static CustomBookingManifestPassengerDto ToPassengerDto(
        BookingPassenger passenger,
        Ticket? ticket,
        bool canCheckInBooking) =>
        new(
            passenger.Id,
            passenger.FullName,
            passenger.DateOfBirth,
            passenger.PassengerType,
            ticket?.Id,
            ticket?.TicketCode,
            ticket?.TicketStatus.ToString(),
            ticket?.CheckedInAt,
            ticket?.CheckedInByUserId,
            ticket?.CheckedInByUser?.FullName,
            ticket?.CheckedOutAt,
            ticket?.CheckedOutByUserId,
            ticket?.CheckedOutByUser?.FullName,
            canCheckInBooking && ticket?.TicketStatus == TicketStatus.Active,
            ticket?.TicketStatus == TicketStatus.CheckedIn && ticket.CheckedInAt.HasValue);
}
