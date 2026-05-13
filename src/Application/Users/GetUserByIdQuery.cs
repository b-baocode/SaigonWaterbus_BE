using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Users;

public sealed record GetUserByIdQuery(int UserId) : IRequest<AuthUserDto>;

public sealed class GetUserByIdQueryValidator : AbstractValidator<GetUserByIdQuery>
{
    public GetUserByIdQueryValidator()
    {
        RuleFor(x => x.UserId)
            .GreaterThan(0)
            .WithMessage("UserId không hợp lệ.");
    }
}

public sealed class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, AuthUserDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetUserByIdQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<AuthUserDto> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);
        var user = await UserManagementSupport.GetVisibleUserByIdAsync(_context, actor, request.UserId, cancellationToken);
        return AuthSupport.CreateUserDto(user);
    }
}
