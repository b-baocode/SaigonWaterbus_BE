using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

public sealed record CheckInTicketCommand(
    string CodeOrToken,
    TicketScanRequestMetadata? Metadata = null) : IRequest<TicketScanDto>;

public sealed class CheckInTicketCommandValidator : AbstractValidator<CheckInTicketCommand>
{
    public CheckInTicketCommandValidator()
    {
        RuleFor(x => x.CodeOrToken).NotEmpty().MaximumLength(100);
    }
}

public sealed class CheckInTicketCommandHandler : IRequestHandler<CheckInTicketCommand, TicketScanDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CheckInTicketCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<TicketScanDto> Handle(CheckInTicketCommand request, CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(currentUser)
            && !AuthSupport.IsManager(currentUser)
            && !AuthSupport.IsStaff(currentUser))
        {
            throw new ForbiddenAccessException();
        }

        var now = _timeProvider.GetUtcNow();
        var metadata = request.Metadata ?? new TicketScanRequestMetadata();
        Domain.Entities.Ticket? ticket = null;
        TicketStatus? ticketStatusBefore = null;

        try
        {
            ticket = await TicketScanSupport.GetTicketAsync(_context, request.CodeOrToken, cancellationToken);
            ticketStatusBefore = ticket.TicketStatus;
            EnsureTicketCanBeCheckedIn(ticket);
        }
        catch (Exception exception) when (TicketScanHistorySupport.IsLoggableFailure(exception))
        {
            await TicketScanHistorySupport.SaveFailureEventAsync(
                _context,
                currentUser,
                TicketScanAction.CheckIn,
                metadata,
                now,
                request.CodeOrToken,
                ticket,
                ticketStatusBefore,
                exception,
                cancellationToken);
            throw;
        }

        ticket!.TicketStatus = TicketStatus.CheckedIn;
        ticket.CheckedInAt = now;
        ticket.CheckedInByUserId = currentUser.Id;
        ticket.CheckedInByUser = currentUser;

        await TicketScanHistorySupport.AddEventAsync(
            _context,
            currentUser,
            TicketScanAction.CheckIn,
            TicketScanResult.Success,
            metadata,
            now,
            request.CodeOrToken,
            ticket,
            ticketStatusBefore,
            ticket.TicketStatus,
            null,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return await TicketScanSupport.ToDtoAsync(_context, ticket, cancellationToken);
    }

    private static void EnsureTicketCanBeCheckedIn(Domain.Entities.Ticket ticket)
    {
        if (ticket.TicketStatus == TicketStatus.CheckedIn)
        {
            throw new ValidationException([new ValidationFailure("ticket", "Ve nay da duoc check-in.")]);
        }

        if (ticket.TicketStatus != TicketStatus.Active)
        {
            throw new ValidationException([new ValidationFailure("ticket", "Ve khong con hieu luc de check-in.")]);
        }

        if (ticket.Booking.BookingStatus != BookingStatus.Confirmed)
        {
            throw new ValidationException([new ValidationFailure("booking", "Booking chua san sang de check-in.")]);
        }

        if (!string.Equals(ticket.Booking.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)
            && ticket.Booking.RemainingAmount > 0)
        {
            throw new ValidationException([new ValidationFailure("payment", "Booking chua thanh toan du de check-in.")]);
        }
    }
}
