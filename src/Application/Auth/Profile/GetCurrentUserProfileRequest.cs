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
        if (!_userContext.UserId.HasValue)
        {
            throw new UnauthorizedAccessException();
        }

        var user = await _context.Set<User>()
            .Include(x => x.Role)
            .Include(x => x.StationAssignments).ThenInclude(a => a.Station)
            .SingleOrDefaultAsync(x => x.Id == _userContext.UserId.Value, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Current user was not found.");

        AuthSupport.EnsureUserCanLogin(user);
        return AuthSupport.CreateUserDto(user);
    }
}
