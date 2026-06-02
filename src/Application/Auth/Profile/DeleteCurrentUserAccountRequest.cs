using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Profile;

public sealed record DeleteCurrentUserAccountRequest();

public sealed class DeleteCurrentUserAccountRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public DeleteCurrentUserAccountRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<AuthActionResultDto> ExecuteAsync(DeleteCurrentUserAccountRequest request, CancellationToken cancellationToken)
    {
        var user = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        AuthSupport.EnsureUserCanLogin(user, requireVerifiedPhone: false);

        if (!AuthSupport.IsCustomer(user))
        {
            throw new ForbiddenAccessException();
        }

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, _timeProvider.GetUtcNow(), cancellationToken);
        _context.Set<User>().Remove(user);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Xoa tai khoan thanh cong.");
    }
}
