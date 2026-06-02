using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Users;

public sealed record GetUserByIdRequest(int UserId);

public sealed class GetUserByIdRequestValidator : AbstractValidator<GetUserByIdRequest>
{
    public GetUserByIdRequestValidator()
    {
        RuleFor(x => x.UserId)
            .GreaterThan(0)
            .WithMessage("UserId không hợp lệ.");
    }
}

public sealed class GetUserByIdRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetUserByIdRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<AuthUserDto> ExecuteAsync(GetUserByIdRequest request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);
        var user = await UserManagementSupport.GetVisibleUserByIdAsync(_context, actor, request.UserId, cancellationToken);
        return AuthSupport.CreateUserDto(user);
    }
}
