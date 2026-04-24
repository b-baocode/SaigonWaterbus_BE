using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Token;

public sealed record LogoutCommand() : IRequest;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public LogoutCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.UserId.HasValue)
        {
            throw new UnauthorizedAccessException();
        }

        var now = _timeProvider.GetUtcNow();
        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, _userContext.UserId.Value, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
