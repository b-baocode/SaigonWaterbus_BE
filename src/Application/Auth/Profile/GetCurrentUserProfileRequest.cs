using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Profile;

public sealed record GetCurrentUserProfileRequest();

public sealed class GetCurrentUserProfileRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCurrentUserProfileRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<AuthUserDto> ExecuteAsync(GetCurrentUserProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        return AuthSupport.CreateUserDto(user);
    }
}
