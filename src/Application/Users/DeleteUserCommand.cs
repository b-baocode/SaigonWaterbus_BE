using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Users;

public sealed record DeleteUserCommand(int UserId) : IRequest<AuthActionResultDto>;

public sealed class DeleteUserCommandValidator : AbstractValidator<DeleteUserCommand>
{
    public DeleteUserCommandValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
    }
}

public sealed class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, AuthActionResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DeleteUserCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<AuthActionResultDto> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        if (_userContext.UserId == request.UserId)
        {
            throw AuthSupport.CreateValidationException(nameof(request.UserId), "Admin cannot delete the current account.");
        }

        var user = await _context.Set<User>()
            .SingleOrDefaultAsync(x => x.Id == request.UserId, cancellationToken)
            ?? throw new global::SaigonWaterbus.Application.Common.Exceptions.NotFoundException("User was not found.");

        _context.Set<User>().Remove(user);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Xoa user thanh cong.");
    }
}
