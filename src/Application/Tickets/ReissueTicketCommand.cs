using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

public sealed record ReissueTicketRequest(string Reason);

public sealed record ReissueTicketCommand(string CodeOrToken, string Reason) : IRequest<TicketScanDto>;

public sealed class ReissueTicketCommandValidator : AbstractValidator<ReissueTicketCommand>
{
    public ReissueTicketCommandValidator()
    {
        RuleFor(x => x.CodeOrToken).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class ReissueTicketCommandHandler : IRequestHandler<ReissueTicketCommand, TicketScanDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ReissueTicketCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<TicketScanDto> Handle(ReissueTicketCommand request, CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(currentUser)
            && !AuthSupport.IsManager(currentUser)
            && !AuthSupport.IsStaff(currentUser))
        {
            throw new ForbiddenAccessException();
        }

        var oldTicket = await TicketScanSupport.GetTicketAsync(_context, request.CodeOrToken, cancellationToken);
        EnsureTicketCanBeReissued(oldTicket);

        var reason = request.Reason.Trim();
        var now = _timeProvider.GetUtcNow();

        oldTicket.TicketStatus = TicketStatus.Cancelled;

        await _context.SaveChangesAsync(cancellationToken);

        var newTicket = new Ticket
        {
            BookingId = oldTicket.BookingId,
            BookingPassengerId = oldTicket.BookingPassengerId,
            TicketCode = await TicketIssueSupport.GenerateTicketCodeAsync(_context, now, cancellationToken),
            QrToken = await TicketIssueSupport.GenerateQrTokenAsync(_context, cancellationToken),
            TicketStatus = TicketStatus.Active,
            IssuedAt = now,
            ReissuedFromTicketId = oldTicket.Id,
            ReissueReason = reason,
            ReissuedAt = now,
            ReissuedByUserId = currentUser.Id,
            ReissuedByUser = currentUser,
            Booking = oldTicket.Booking,
            BookingPassenger = oldTicket.BookingPassenger
        };

        _context.Tickets.Add(newTicket);
        await _context.SaveChangesAsync(cancellationToken);

        return await TicketScanSupport.ToDtoAsync(_context, newTicket, cancellationToken);
    }

    private static void EnsureTicketCanBeReissued(Ticket ticket)
    {
        if (ticket.TicketStatus != TicketStatus.Active)
        {
            throw new ValidationException([new ValidationFailure("ticket", "Chi co the cap lai ve dang Active.")]);
        }

        if (ticket.Booking.BookingStatus != BookingStatus.Confirmed)
        {
            throw new ValidationException([new ValidationFailure("booking", "Booking chua san sang de cap lai ve.")]);
        }

        if (!string.Equals(ticket.Booking.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)
            && ticket.Booking.RemainingAmount > 0)
        {
            throw new ValidationException([new ValidationFailure("payment", "Booking chua thanh toan du de cap lai ve.")]);
        }
    }
}
