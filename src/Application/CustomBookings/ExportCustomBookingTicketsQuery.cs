using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CustomBookings;

public sealed record ExportCustomBookingTicketsQuery(
    Guid BookingId,
    IReadOnlyCollection<Guid>? TicketIds = null)
    : IRequest<CustomBookingTicketExportDto>;

public sealed record ExportCustomBookingTicketsByQrTokenQuery(string QrToken)
    : IRequest<CustomBookingTicketExportDto>;

public sealed class ExportCustomBookingTicketsQueryValidator
    : AbstractValidator<ExportCustomBookingTicketsQuery>
{
    public ExportCustomBookingTicketsQueryValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleForEach(x => x.TicketIds)
            .NotEmpty()
            .When(x => x.TicketIds is not null);
    }
}

public sealed class ExportCustomBookingTicketsByQrTokenQueryValidator
    : AbstractValidator<ExportCustomBookingTicketsByQrTokenQuery>
{
    public ExportCustomBookingTicketsByQrTokenQueryValidator()
    {
        RuleFor(x => x.QrToken).NotEmpty().MaximumLength(100);
    }
}

public sealed class ExportCustomBookingTicketsQueryHandler
    : IRequestHandler<ExportCustomBookingTicketsQuery, CustomBookingTicketExportDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public ExportCustomBookingTicketsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CustomBookingTicketExportDto> Handle(
        ExportCustomBookingTicketsQuery request,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(
            _context,
            _userContext,
            cancellationToken);

        var booking = await CustomBookingQuerySupport.BuildBaseQuery(_context)
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Custom booking not found.");

        if (booking.UserId != currentUser.Id
            && !AuthSupport.IsAdmin(currentUser)
            && !AuthSupport.IsManager(currentUser)
            && !AuthSupport.IsStaff(currentUser))
        {
            throw new NotFoundException("Custom booking not found.");
        }

        return CustomBookingTicketExportSupport.ToDto(booking, request.TicketIds);
    }
}

public sealed class ExportCustomBookingTicketsByQrTokenQueryHandler
    : IRequestHandler<ExportCustomBookingTicketsByQrTokenQuery, CustomBookingTicketExportDto>
{
    private readonly IApplicationDbContext _context;

    public ExportCustomBookingTicketsByQrTokenQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CustomBookingTicketExportDto> Handle(
        ExportCustomBookingTicketsByQrTokenQuery request,
        CancellationToken cancellationToken)
    {
        var qrToken = request.QrToken.Trim();
        var bookingId = await _context.Tickets
            .AsNoTracking()
            .Where(x => x.QrToken == qrToken)
            .Select(x => (Guid?)x.BookingId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Custom booking ticket not found.");

        var booking = await CustomBookingQuerySupport.BuildBaseQuery(_context)
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Custom booking not found.");

        return CustomBookingTicketExportSupport.ToDto(booking, ticketIds: null);
    }
}

internal static class CustomBookingTicketExportSupport
{
    private const string PaidBookingPaymentStatus = "Paid";

    public static CustomBookingTicketExportDto ToDto(
        Booking booking,
        IReadOnlyCollection<Guid>? ticketIds)
    {
        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PaymentStatus),
                "Chỉ export vé sau khi custom booking đã thanh toán đủ.")]);
        }

        var tickets = ApplyTicketSelection(
            CustomBookingTicketSupport.GetDisplayTickets(booking.Tickets),
            ticketIds);
        return ToDto(booking, tickets);
    }

    public static CustomBookingTicketExportDto ToDto(
        Booking booking,
        IReadOnlyList<Ticket> tickets)
    {
        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PaymentStatus),
                "Chỉ export vé sau khi custom booking đã thanh toán đủ.")]);
        }

        if (tickets.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("tickets",
                "Custom booking chưa có vé để export. Hãy nhập hoặc upload danh sách hành khách trước.")]);
        }

        return new CustomBookingTicketExportDto(
            booking.Id,
            booking.BookingCode,
            booking.DepartureDate,
            booking.StartTime,
            booking.Boat?.Name,
            booking.FromStation?.StationName,
            booking.ToStation?.StationName,
            CustomBookingManifestSupport.ToItineraryStopDtos(booking),
            tickets
                .Select(x => new CustomBookingTicketExportItemDto(
                    x.Id,
                    x.BookingPassengerId,
                    x.BookingPassenger?.FullName,
                    x.BookingPassenger?.DateOfBirth,
                    x.BookingPassenger?.PassengerType,
                    x.TicketCode,
                    x.QrToken,
                    x.TicketStatus.ToString()))
                .ToList());
    }

    private static IReadOnlyList<Ticket> ApplyTicketSelection(
        IReadOnlyList<Ticket> tickets,
        IReadOnlyCollection<Guid>? ticketIds)
    {
        if (ticketIds is not { Count: > 0 })
        {
            return tickets;
        }

        var selectedTicketIds = ticketIds.Distinct().ToArray();
        var ticketsById = tickets.ToDictionary(x => x.Id);
        var missingTicketIds = selectedTicketIds
            .Where(x => !ticketsById.ContainsKey(x))
            .ToArray();
        if (missingTicketIds.Length > 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(ExportCustomBookingTicketsQuery.TicketIds),
                "Danh sách ticketIds có vé không thuộc custom booking hoặc không còn hợp lệ để export.")]);
        }

        return selectedTicketIds
            .Select(x => ticketsById[x])
            .ToList();
    }
}
