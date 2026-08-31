using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record ExportCharterBookingTicketsQuery(
    Guid BookingId,
    IReadOnlyCollection<Guid>? TicketIds = null)
    : IRequest<CharterBookingTicketExportDto>;

public sealed record ExportCharterBookingTicketsByQrTokenQuery(string QrToken)
    : IRequest<CharterBookingTicketExportDto>;

public sealed class ExportCharterBookingTicketsQueryValidator
    : AbstractValidator<ExportCharterBookingTicketsQuery>
{
    public ExportCharterBookingTicketsQueryValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleForEach(x => x.TicketIds)
            .NotEmpty()
            .When(x => x.TicketIds is not null);
    }
}

public sealed class ExportCharterBookingTicketsByQrTokenQueryValidator
    : AbstractValidator<ExportCharterBookingTicketsByQrTokenQuery>
{
    public ExportCharterBookingTicketsByQrTokenQueryValidator()
    {
        RuleFor(x => x.QrToken).NotEmpty().MaximumLength(100);
    }
}

public sealed class ExportCharterBookingTicketsQueryHandler
    : IRequestHandler<ExportCharterBookingTicketsQuery, CharterBookingTicketExportDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public ExportCharterBookingTicketsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CharterBookingTicketExportDto> Handle(
        ExportCharterBookingTicketsQuery request,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(
            _context,
            _userContext,
            cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.CharterBoats)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
                    .ThenInclude(x => x!.TripSeat)
                        .ThenInclude(x => x!.Seat)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        await CharterBookingAssignmentSupport.EnsureCanViewOperationalAsync(
            _context,
            currentUser,
            booking,
            includeCustomerOwner: true,
            notFoundWhenDenied: true,
            cancellationToken);

        return CharterBookingTicketExportSupport.ToDto(booking, request.TicketIds);
    }
}

public sealed class ExportCharterBookingTicketsByQrTokenQueryHandler
    : IRequestHandler<ExportCharterBookingTicketsByQrTokenQuery, CharterBookingTicketExportDto>
{
    private readonly IApplicationDbContext _context;

    public ExportCharterBookingTicketsByQrTokenQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CharterBookingTicketExportDto> Handle(
        ExportCharterBookingTicketsByQrTokenQuery request,
        CancellationToken cancellationToken)
    {
        var qrToken = request.QrToken.Trim();
        var bookingId = await _context.Tickets
            .AsNoTracking()
            .Where(x => x.QrToken == qrToken)
            .Select(x => (Guid?)x.BookingId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Charter booking ticket not found.");

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
                    .ThenInclude(x => x!.TripSeat)
                        .ThenInclude(x => x!.Seat)
            .SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        return CharterBookingTicketExportSupport.ToDto(booking, ticketIds: null);
    }
}

internal static class CharterBookingTicketExportSupport
{
    private const string PaidBookingPaymentStatus = BookingPaymentStatusExtensions.PaidValue;

    public static CharterBookingTicketExportDto ToDto(
        Booking booking,
        IReadOnlyCollection<Guid>? ticketIds)
    {
        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PaymentStatus),
                "Chỉ export vé sau khi charter booking đã thanh toán đủ.")]);
        }

        var tickets = ApplyTicketSelection(
            CharterBookingTicketSupport.GetDisplayTickets(booking.Tickets),
            ticketIds);
        return ToDto(booking, tickets);
    }

    public static CharterBookingTicketExportDto ToDto(
        Booking booking,
        IReadOnlyList<Ticket> tickets)
    {
        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PaymentStatus),
                "Chỉ export vé sau khi charter booking đã thanh toán đủ.")]);
        }

        if (tickets.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("tickets",
                "Charter booking chưa có vé để export. Hãy nhập hoặc upload danh sách hành khách trước.")]);
        }

        return new CharterBookingTicketExportDto(
            booking.Id,
            booking.BookingCode,
            booking.DepartureDate,
            booking.StartTime,
            booking.Boat?.Name,
            booking.FromStation?.StationName,
            booking.ToStation?.StationName,
            CharterBookingManifestSupport.ToItineraryStopDtos(booking),
            tickets
                .Select(x => new CharterBookingTicketExportItemDto(
                    x.Id,
                    x.BookingPassengerId,
                    x.BookingPassenger?.FullName,
                    x.BookingPassenger?.BirthYear,
                    x.BookingPassenger?.PassengerType,
                    x.TicketCode,
                    x.QrToken,
                    x.TicketStatus.ToString(),
                    x.BookingPassenger?.TripSeat?.Seat?.Code))
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
            throw new ValidationException([new ValidationFailure(nameof(ExportCharterBookingTicketsQuery.TicketIds),
                "Danh sách ticketIds có vé không thuộc charter booking hoặc không còn hợp lệ để export.")]);
        }

        return selectedTicketIds
            .Select(x => ticketsById[x])
            .ToList();
    }
}
