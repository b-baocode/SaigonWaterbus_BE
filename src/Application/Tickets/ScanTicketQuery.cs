using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

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
        var ticket = await TicketScanSupport.GetTicketAsync(_context, request.CodeOrToken, cancellationToken);
        TicketScanSupport.EnsureCanViewTicket(currentUser, ticket);
        return await TicketScanSupport.ToDtoAsync(_context, ticket, cancellationToken);
    }
}
