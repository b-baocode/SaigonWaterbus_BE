using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Profile;

public sealed record GetCurrentUserProfileQuery() : IRequest<AuthUserDto>;

public sealed class GetCurrentUserProfileQueryHandler : IRequestHandler<GetCurrentUserProfileQuery, AuthUserDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCurrentUserProfileQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<AuthUserDto> Handle(GetCurrentUserProfileQuery request, CancellationToken cancellationToken)
    {
        if (!_userContext.UserId.HasValue)
        {
            throw new UnauthorizedAccessException();
        }

        var user = await _context.Set<User>()
            .SingleOrDefaultAsync(x => x.Id == _userContext.UserId.Value, cancellationToken)
            ?? throw new global::SaigonWaterbus.Application.Common.Exceptions.NotFoundException("User profile was not found.");

        var roles = await AuthSupport.GetActiveRolesAsync(_context, user.Id, cancellationToken);
        var roleDtos = roles
            .Select(x => new AuthRoleDto(x.Code, x.SystemName, x.DisplayName))
            .ToArray();

        return new AuthUserDto(
            user.Id,
            user.UserCode,
            user.FullName,
            user.DateOfBirth,
            user.PhoneNumber,
            user.Email,
            user.Status,
            roleDtos);
    }
}
