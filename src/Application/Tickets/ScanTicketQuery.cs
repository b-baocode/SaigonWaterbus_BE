using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Tickets;

public sealed record ScanTicketQuery(
    string CodeOrToken,
    TicketScanRequestMetadata? Metadata = null) : IRequest<TicketScanDto>;

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
    private readonly TimeProvider _timeProvider;

    public ScanTicketQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<TicketScanDto> Handle(ScanTicketQuery request, CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var metadata = request.Metadata ?? new TicketScanRequestMetadata();
        var now = _timeProvider.GetUtcNow();
        Ticket? ticket = null;

        try
        {
            ticket = await TicketScanSupport.GetTicketAsync(_context, request.CodeOrToken, cancellationToken);
            TicketScanSupport.EnsureCanViewTicket(currentUser, ticket);
            await TicketStaffScanAuthorizationSupport.EnsureStaffCanOperateTicketAsync(
                _context, currentUser, ticket, now, cancellationToken);
        }
        catch (Exception exception) when (
            TicketScanHistorySupport.IsOperationalUser(currentUser)
            && TicketScanHistorySupport.IsLoggableFailure(exception))
        {
            await TicketScanHistorySupport.SaveFailureEventAsync(
                _context,
                currentUser,
                TicketScanAction.Scan,
                metadata,
                now,
                request.CodeOrToken,
                ticket,
                ticket?.TicketStatus,
                exception,
                cancellationToken);
            throw;
        }

        if (TicketScanHistorySupport.IsOperationalUser(currentUser))
        {
            await TicketScanHistorySupport.AddEventAsync(
                _context,
                currentUser,
                TicketScanAction.Scan,
                TicketScanResult.Success,
                metadata,
                now,
                request.CodeOrToken,
                ticket,
                ticket!.TicketStatus,
                ticket.TicketStatus,
                null,
                cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return await TicketScanSupport.ToDtoAsync(_context, ticket!, cancellationToken, now);
    }
}
