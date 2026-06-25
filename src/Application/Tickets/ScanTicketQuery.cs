using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

public sealed record ScanTicketQuery(string CodeOrToken) : IRequest<TicketScanDto>;

public sealed class ScanTicketQueryValidator : AbstractValidator<ScanTicketQuery>
{
    public ScanTicketQueryValidator()
    {
        RuleFor(x => x.CodeOrToken).NotEmpty().MaximumLength(100);
    }
}

public sealed class ScanTicketQueryHandler : IRequestHandler<ScanTicketQuery, TicketScanDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public ScanTicketQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<TicketScanDto> Handle(ScanTicketQuery request, CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var canScanAnyTicket = AuthSupport.IsAdmin(currentUser)
            || AuthSupport.IsManager(currentUser)
            || AuthSupport.IsStaff(currentUser);

        var codeOrToken = request.CodeOrToken.Trim();
        var ticket = await _context.Tickets
            .Include(x => x.Booking)
                .ThenInclude(x => x.Passengers)
            .SingleOrDefaultAsync(x => x.TicketCode == codeOrToken || x.QrToken == codeOrToken, cancellationToken)
            ?? throw new NotFoundException("Ticket not found.");

        if (!canScanAnyTicket && ticket.Booking.UserId != currentUser.Id)
        {
            throw new NotFoundException("Ticket not found.");
        }

        if (ticket.Booking is CustomBooking)
        {
            var customBooking = await _context.Set<CustomBooking>()
                .Include(x => x.Vessel)
                .Include(x => x.FromStation)
                .Include(x => x.ToStation)
                .Include(x => x.Passengers)
                .SingleAsync(x => x.Id == ticket.BookingId, cancellationToken);

            return ToCustomBookingScanDto(ticket, customBooking);
        }

        return ToBookingScanDto(ticket, ticket.Booking);
    }

    private static TicketScanDto ToCustomBookingScanDto(BookingTicket ticket, CustomBooking booking) =>
        new(
            ticket.Id,
            ticket.TicketCode,
            ticket.QrToken,
            ticket.TicketTypeCode,
            ticket.TicketTypeName,
            ticket.TicketStatus.ToString(),
            ticket.IssuedAt,
            ticket.CheckedInAt,
            booking.Id,
            booking.BookingCode,
            nameof(CustomBooking),
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.ContactName,
            booking.ContactPhone,
            booking.ContactEmail,
            booking.PassengerCount,
            booking.Passengers.Count,
            booking.AdultCount,
            booking.ChildCount,
            booking.DepartureDate,
            booking.StartTime,
            booking.Vessel?.Name,
            booking.FromStation?.StationName,
            booking.ToStation?.StationName,
            booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(ToPassengerDto)
                .ToList());

    private static TicketScanDto ToBookingScanDto(BookingTicket ticket, Booking booking) =>
        new(
            ticket.Id,
            ticket.TicketCode,
            ticket.QrToken,
            ticket.TicketTypeCode,
            ticket.TicketTypeName,
            ticket.TicketStatus.ToString(),
            ticket.IssuedAt,
            ticket.CheckedInAt,
            booking.Id,
            booking.BookingCode,
            nameof(Booking),
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.ContactName,
            booking.ContactPhone,
            booking.ContactEmail,
            booking.Passengers.Count,
            booking.Passengers.Count,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(ToPassengerDto)
                .ToList());

    private static TicketScanPassengerDto ToPassengerDto(BookingPassenger passenger) =>
        new(passenger.FullName, passenger.DateOfBirth, passenger.PassengerType);
}
