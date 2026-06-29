using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Users;

public sealed record CreateUserRequest(
    string FullName,
    DateOnly? DateOfBirth,
    string? PhoneNumber,
    string Email,
    Guid RoleId,
    string? Gender = null,
    string? Nationality = null);

public sealed record ManagedUserPasswordResultDto(
    AuthUserDto User,
    string GeneratedPassword);

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .WithMessage("Họ và tên không được để trống.")
            .MaximumLength(150)
            .WithMessage("Họ và tên không được vượt quá 150 ký tự.");

        RuleFor(x => x.DateOfBirth)
            .Must(x => !x.HasValue || x.Value <= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("Ngày sinh không được lớn hơn ngày hiện tại.");

        RuleFor(x => x.Gender)
            .MaximumLength(30)
            .WithMessage("Giới tính không được vượt quá 30 ký tự.");

        RuleFor(x => x.Nationality)
            .MaximumLength(100)
            .WithMessage("Quốc tịch không được vượt quá 100 ký tự.");

        RuleFor(x => x.PhoneNumber)
            .Must(phoneNumber => phoneNumber is null || PhoneRules.IsValid(phoneNumber))
            .WithMessage(PhoneRules.InvalidInternationalPhoneMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));

        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Email là bắt buộc.")
            .MaximumLength(255)
            .WithMessage("Email không được vượt quá 255 ký tự.")
            .EmailAddress()
            .WithMessage("Email không đúng định dạng.")
            .Must(EmailRules.HasAllowedRegistrationDomain)
            .WithMessage(EmailRules.AllowedEmailDomainMessage);

        RuleFor(x => x.RoleId)
            .NotEmpty()
            .WithMessage("Vai trò là bắt buộc.");

    }
}

public sealed class CreateUserRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserCodeGenerator _userCodeGenerator;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CreateUserRequestUseCase(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        IUserCodeGenerator userCodeGenerator,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _userCodeGenerator = userCodeGenerator;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<ManagedUserPasswordResultDto> ExecuteAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);
        var role = await AuthSupport.GetRoleByIdAsync(_context, request.RoleId, nameof(request.RoleId), cancellationToken);

        UserManagementSupport.EnsureCanCreateRole(actor, role, nameof(request.RoleId));

        var normalizedEmail = _identityNormalizer.NormalizeEmail(request.Email);
        if (await AuthSupport.WhereUserIdentityMatches(_context.Set<User>(), null, normalizedEmail)
                .AnyAsync(cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Email), "Email đã được đăng ký.");
        }

        var normalizedPhone = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : _identityNormalizer.NormalizePhone(request.PhoneNumber);

        if (normalizedPhone is not null
            && await AuthSupport.WhereUserIdentityMatches(_context.Set<User>(), normalizedPhone, null)
                .AnyAsync(cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.PhoneNumber), "Số điện thoại đã được đăng ký.");
        }

        var generatedPassword = ManagedUserPasswordSupport.GeneratePassword();
        var user = new User
        {
            FullName = request.FullName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Gender = AuthSupport.NormalizeOptionalText(request.Gender),
            Nationality = AuthSupport.NormalizeOptionalText(request.Nationality),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                ? null
                : PhoneRules.ToInternationalFormat(request.PhoneNumber),
            NormalizedPhoneNumber = normalizedPhone,
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = _secretHasher.Hash(generatedPassword),
            RoleId = role.Id,
            Status = UserStatus.Active
        };

        var now = _timeProvider.GetUtcNow();
        if (user.Status == UserStatus.Active && user.PhoneNumber is not null)
        {
            user.PhoneVerifiedAt = now;
        }

        user.UserCode = await _userCodeGenerator.GenerateNextCodeAsync(role.Code, cancellationToken);

        await _context.ExecuteInTransactionAsync(async ct =>
        {
            _context.Set<User>().Add(user);
            await _context.SaveChangesAsync(ct);
        }, cancellationToken);

        var createdUser = await _context.Set<User>()
            .Include(x => x.Role)
            .Include(x => x.StationAssignments).ThenInclude(a => a.Station)
            .SingleAsync(x => x.Id == user.Id, cancellationToken);

        return new ManagedUserPasswordResultDto(AuthSupport.CreateUserDto(createdUser), generatedPassword);
    }
}
