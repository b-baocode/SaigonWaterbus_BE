using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Users;

public sealed record CreateUserCommand(
    string FullName,
    DateOnly? DateOfBirth,
    string? PhoneNumber,
    string Email,
    string Password,
    int RoleId,
    string? Department,
    UserStatus? Status) : IRequest<AuthUserDto>;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
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

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8);

        RuleFor(x => x.RoleId)
            .GreaterThan(0);

        RuleFor(x => x.Department)
            .MaximumLength(100)
            .When(x => !string.IsNullOrWhiteSpace(x.Department));
    }
}

public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, AuthUserDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserCodeGenerator _userCodeGenerator;
    private readonly IUserContext _userContext;

    public CreateUserCommandHandler(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        IUserCodeGenerator userCodeGenerator,
        IUserContext userContext)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _userCodeGenerator = userCodeGenerator;
        _userContext = userContext;
    }

    public async Task<AuthUserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);
        var role = await AuthSupport.GetRoleByIdAsync(_context, request.RoleId, nameof(request.RoleId), cancellationToken);

        UserManagementSupport.EnsureCanCreateRole(actor, role, nameof(request.RoleId));
        UserManagementSupport.EnsureDepartmentMatchesRole(role, request.Department, nameof(request.Department));

        var normalizedEmail = _identityNormalizer.NormalizeEmail(request.Email);
        if (await _context.Set<User>().AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Email), "Email is already registered.");
        }

        var normalizedPhone = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : _identityNormalizer.NormalizePhone(request.PhoneNumber);

        if (normalizedPhone is not null
            && await _context.Set<User>().AnyAsync(x => x.NormalizedPhoneNumber == normalizedPhone, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.PhoneNumber), "Phone number is already registered.");
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            DateOfBirth = request.DateOfBirth,
            PhoneNumber = request.PhoneNumber?.Trim(),
            NormalizedPhoneNumber = normalizedPhone,
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = _secretHasher.Hash(request.Password),
            RoleId = role.Id,
            Department = request.Department?.Trim(),
            Status = request.Status ?? UserStatus.Active
        };

        user.UserCode = await _userCodeGenerator.GenerateNextCodeAsync(role.Code, cancellationToken);

        _context.Set<User>().Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        var createdUser = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleAsync(x => x.Id == user.Id, cancellationToken);

        return AuthSupport.CreateUserDto(createdUser);
    }
}
