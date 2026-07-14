using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record GetCharterBookingManifestByCodeQuery(string BookingCode)
    : IRequest<CharterBookingManifestDto>;

public sealed record GetCharterBookingManifestByQrTokenQuery(string QrToken)
    : IRequest<CharterBookingManifestDto>;

public sealed class GetCharterBookingManifestByCodeQueryValidator
    : AbstractValidator<GetCharterBookingManifestByCodeQuery>
{
    public GetCharterBookingManifestByCodeQueryValidator()
    {
        RuleFor(x => x.BookingCode).NotEmpty().MaximumLength(50);
    }
}

public sealed class GetCharterBookingManifestByQrTokenQueryValidator
    : AbstractValidator<GetCharterBookingManifestByQrTokenQuery>
{
    public GetCharterBookingManifestByQrTokenQueryValidator()
    {
        RuleFor(x => x.QrToken).NotEmpty().MaximumLength(100);
    }
}

public sealed class GetCharterBookingManifestByCodeQueryHandler
    : IRequestHandler<GetCharterBookingManifestByCodeQuery, CharterBookingManifestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCharterBookingManifestByCodeQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CharterBookingManifestDto> Handle(
        GetCharterBookingManifestByCodeQuery request,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(
            _context,
            _userContext,
            cancellationToken);
        var normalizedBookingCode = request.BookingCode.Trim().ToUpperInvariant();

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.CharterBoats)
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
            ?? throw new NotFoundException("Charter booking not found.");

        await CharterBookingAssignmentSupport.EnsureCanViewOperationalAsync(
            _context,
            currentUser,
            booking,
            includeCustomerOwner: true,
            notFoundWhenDenied: true,
            cancellationToken);
        return CharterBookingManifestSupport.ToDto(booking);
    }
}

public sealed class GetCharterBookingManifestByQrTokenQueryHandler
    : IRequestHandler<GetCharterBookingManifestByQrTokenQuery, CharterBookingManifestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCharterBookingManifestByQrTokenQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CharterBookingManifestDto> Handle(
        GetCharterBookingManifestByQrTokenQuery request,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(
            _context,
            _userContext,
            cancellationToken);
        var qrToken = request.QrToken.Trim();

        var booking = await CharterBookingManifestSupport.BuildQuery(_context)
            .SingleOrDefaultAsync(
                x => x.CharterBookingQrToken == qrToken,
                cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        await CharterBookingAssignmentSupport.EnsureCanViewOperationalAsync(
            _context,
            currentUser,
            booking,
            includeCustomerOwner: true,
            notFoundWhenDenied: true,
            cancellationToken);
        return CharterBookingManifestSupport.ToDto(booking);
    }
}

internal static class CharterBookingManifestSupport
{
    private const string PaidBookingPaymentStatus = "Paid";

    public static IQueryable<Booking> BuildQuery(IApplicationDbContext context) =>
        CharterBookingQuerySupport.BuildBaseQuery(context)
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.CharterBoats)
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
                .ThenInclude(x => x.CheckedOutByUser);

    public static CharterBookingManifestDto ToDto(Booking booking)
    {
        var currentTickets = CharterBookingTicketSupport.GetDisplayTickets(booking.Tickets);
        var ticketsByPassengerId = currentTickets
            .Where(x => x.BookingPassengerId.HasValue)
            .ToDictionary(x => x.BookingPassengerId!.Value);
        var canCheckInBooking = booking.BookingStatus == BookingStatus.Confirmed
            && (string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase)
                || booking.RemainingAmount <= 0);

        var approvedPassengers = booking.Passengers
            .Where(CharterBookingPassengerSupport.IsApproved)
            .ToList();

        return new CharterBookingManifestDto(
            booking.Id,
            booking.BookingCode,
            booking.CharterBookingQrToken,
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
            approvedPassengers.Count,
            CharterBookingPassengerSupport.CountAdults(approvedPassengers),
            CharterBookingPassengerSupport.CountChildren(approvedPassengers),
            new CharterBookingTicketSummaryDto(
                currentTickets.Count,
                currentTickets.Count(x => x.TicketStatus == TicketStatus.Active),
                currentTickets.Count(x => x.TicketStatus == TicketStatus.CheckedIn),
                currentTickets.Count(x => x.TicketStatus == TicketStatus.CheckedOut)),
            approvedPassengers
                .OrderBy(x => x.FullName)
                .ThenBy(x => x.Id)
                .Select(passenger =>
                {
                    ticketsByPassengerId.TryGetValue(passenger.Id, out var ticket);
                    return ToPassengerDto(passenger, ticket, canCheckInBooking);
                })
                .ToList());
    }

    public static IReadOnlyList<CharterBookingItineraryStopDto> ToItineraryStopDtos(Booking booking) =>
        booking.ItineraryStops
            .OrderBy(x => x.StopOrder)
            .Select(x => new CharterBookingItineraryStopDto(
                x.StationId,
                x.Station.StationName,
                x.StopOrder,
                x.StayDurationMinutes,
                x.Note))
            .ToList();

    private static CharterBookingManifestPassengerDto ToPassengerDto(
        BookingPassenger passenger,
        Ticket? ticket,
        bool canCheckInBooking) =>
        new(
            passenger.Id,
            passenger.FullName,
            passenger.DateOfBirth,
            passenger.BirthYear,
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
