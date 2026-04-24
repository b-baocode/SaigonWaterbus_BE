using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Users;

public sealed record UpdateUserRoleCommand(int UserId, string RoleCode) : IRequest<AuthUserDto>;

public sealed class UpdateUserRoleCommandValidator : AbstractValidator<UpdateUserRoleCommand>
{
    public UpdateUserRoleCommandValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.RoleCode)
            .NotEmpty()
            .MaximumLength(10);
    }
}

public sealed class UpdateUserRoleCommandHandler : IRequestHandler<UpdateUserRoleCommand, AuthUserDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserCodeGenerator _userCodeGenerator;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateUserRoleCommandHandler(
        IApplicationDbContext context,
        IUserCodeGenerator userCodeGenerator,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userCodeGenerator = userCodeGenerator;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<AuthUserDto> Handle(UpdateUserRoleCommand request, CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        var user = await _context.Set<User>()
            .SingleOrDefaultAsync(x => x.Id == request.UserId, cancellationToken)
            ?? throw new global::SaigonWaterbus.Application.Common.Exceptions.NotFoundException("User was not found.");

        var roleCode = request.RoleCode.Trim().ToUpperInvariant();
        var role = await _context.Set<Role>()
            .SingleOrDefaultAsync(x => x.Code == roleCode, cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.RoleCode), "Role code is invalid.");

        var activeAssignments = await _context.Set<UserRoleAssignment>()
            .Where(x => x.UserId == user.Id && x.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var assignment in activeAssignments)
        {
            assignment.IsActive = false;
        }

        var now = _timeProvider.GetUtcNow();
        var targetPrefix = UserCodes.GetPrefixForRoleCode(role.Code);
        var existingAssignment = await _context.Set<UserRoleAssignment>()
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.RoleId == role.Id, cancellationToken);

        if (existingAssignment is null)
        {
            _context.Set<UserRoleAssignment>().Add(new UserRoleAssignment
            {
                UserId = user.Id,
                RoleId = role.Id,
                ScopeType = role.DefaultScopeType,
                AssignedAt = now,
                AssignedByUserId = _userContext.UserId,
                IsActive = true
            });
        }
        else
        {
            existingAssignment.IsActive = true;
            existingAssignment.ScopeType = role.DefaultScopeType;
            existingAssignment.ScopeEntityId = null;
            existingAssignment.AssignedAt = now;
            existingAssignment.AssignedByUserId = _userContext.UserId;
        }

        if (!UserCodes.HasPrefix(user.UserCode, targetPrefix))
        {
            user.UserCode = await _userCodeGenerator.GenerateNextCodeAsync(role.Code, cancellationToken);
        }

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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
