using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Users;

public sealed record UpdateUserCommand(
    int UserId,
    string FullName,
    DateOnly? DateOfBirth,
    string? PhoneNumber,
    string Email,
    int RoleId,
    string? Department,
    UserStatus Status) : IRequest<AuthUserDto>;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);

        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.DateOfBirth)
            .Must(x => !x.HasValue || x.Value <= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("Date of birth cannot be in the future.");

        RuleFor(x => x.PhoneNumber)
            .Must(phoneNumber => phoneNumber is null || PhoneRules.IsValid(phoneNumber))
            .WithMessage("Phone number must contain exactly 10 digits.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.RoleId)
            .GreaterThan(0);

        RuleFor(x => x.Department)
            .MaximumLength(100)
            .When(x => !string.IsNullOrWhiteSpace(x.Department));
    }
}

public sealed class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, AuthUserDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly IUserCodeGenerator _userCodeGenerator;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateUserCommandHandler(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        IUserCodeGenerator userCodeGenerator,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _userCodeGenerator = userCodeGenerator;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<AuthUserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);
        var user = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == request.UserId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("User was not found.");

        UserManagementSupport.EnsureCanUpdateUser(actor, user);

        var targetRole = await AuthSupport.GetRoleByIdAsync(_context, request.RoleId, nameof(request.RoleId), cancellationToken);
        UserManagementSupport.EnsureCanAssignRole(actor, user, targetRole, nameof(request.RoleId));
        UserManagementSupport.EnsureDepartmentMatchesRole(targetRole, request.Department, nameof(request.Department));

        var normalizedEmail = _identityNormalizer.NormalizeEmail(request.Email);
        if (await _context.Set<User>().AnyAsync(x => x.NormalizedEmail == normalizedEmail && x.Id != user.Id, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Email), "Email is already registered.");
        }

        var normalizedPhone = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : _identityNormalizer.NormalizePhone(request.PhoneNumber);

        if (normalizedPhone is not null
            && await _context.Set<User>().AnyAsync(
                x => x.NormalizedPhoneNumber == normalizedPhone && x.Id != user.Id,
                cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.PhoneNumber), "Phone number is already registered.");
        }

        user.FullName = request.FullName.Trim();
        user.DateOfBirth = request.DateOfBirth;
        user.PhoneNumber = request.PhoneNumber?.Trim();
        user.NormalizedPhoneNumber = normalizedPhone;
        user.Email = request.Email.Trim();
        user.NormalizedEmail = normalizedEmail;
        user.Department = request.Department?.Trim();
        user.Status = request.Status;

        if (user.RoleId != targetRole.Id)
        {
            user.RoleId = targetRole.Id;

            if (!UserCodes.HasPrefix(user.UserCode, UserCodes.GetPrefixForRoleCode(targetRole.Code)))
            {
                user.UserCode = await _userCodeGenerator.GenerateNextCodeAsync(targetRole.Code, cancellationToken);
            }
        }

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, _timeProvider.GetUtcNow(), cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        var updatedUser = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleAsync(x => x.Id == user.Id, cancellationToken);

        return AuthSupport.CreateUserDto(updatedUser);
    }
}
