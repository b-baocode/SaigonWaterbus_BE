using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Token;

public sealed record LogoutRequest();

public sealed class LogoutRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public LogoutRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task ExecuteAsync(LogoutRequest request, CancellationToken cancellationToken)
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
