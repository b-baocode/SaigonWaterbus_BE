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
            .WithMessage("Họ và tên không được để trống.")
            .MaximumLength(150)
            .WithMessage("Họ và tên không được vượt quá 150 ký tự.");

        RuleFor(x => x.DateOfBirth)
            .Must(x => !x.HasValue || x.Value <= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("Ngày sinh không được lớn hơn ngày hiện tại.");

        RuleFor(x => x.PhoneNumber)
            .Must(phoneNumber => phoneNumber is null || PhoneRules.IsValid(phoneNumber))
            .WithMessage(PhoneRules.InvalidInternationalPhoneMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));

        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email là bắt buộc.")
            .MaximumLength(255)
            .WithMessage("Email không được vượt quá 255 ký tự.")
            .EmailAddress()
            .WithMessage("Email không đúng định dạng.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Mật khẩu là bắt buộc.")
            .Must(PasswordRules.IsStrong)
            .WithMessage(PasswordRules.StrongPasswordMessage);

        RuleFor(x => x.RoleId)
            .GreaterThan(0)
            .WithMessage("Vai trò là bắt buộc.");

        RuleFor(x => x.Department)
            .MaximumLength(100)
            .WithMessage("Phòng ban không được vượt quá 100 ký tự.")
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
            throw AuthSupport.CreateValidationException(nameof(request.Email), "Email đã được đăng ký.");
        }

        var normalizedPhone = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : _identityNormalizer.NormalizePhone(request.PhoneNumber);

        if (normalizedPhone is not null
            && await _context.Set<User>().AnyAsync(x => x.NormalizedPhoneNumber == normalizedPhone, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.PhoneNumber), "Số điện thoại đã được đăng ký.");
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            DateOfBirth = request.DateOfBirth,
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                ? null
                : PhoneRules.ToInternationalFormat(request.PhoneNumber),
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
