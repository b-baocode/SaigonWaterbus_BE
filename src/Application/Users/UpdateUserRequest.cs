using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Users;

public sealed record UpdateUserRequest(
    int UserId,
    string? FullName,
    DateOnly? DateOfBirth,
    string? PhoneNumber,
    string? Email,
    int? RoleId);

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.UserId)
            .GreaterThan(0)
            .WithMessage("UserId không hợp lệ.");

        RuleFor(x => x.FullName)
            .NotEmpty()
            .WithMessage("Họ và tên không được để trống.")
            .MaximumLength(150)
            .WithMessage("Họ và tên không được vượt quá 150 ký tự.")
            .When(x => x.FullName is not null);

        RuleFor(x => x.DateOfBirth)
            .Must(x => !x.HasValue || x.Value <= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("Ngày sinh không được lớn hơn ngày hiện tại.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Số điện thoại không được để trống.")
            .Must(PhoneRules.IsValid)
            .WithMessage(PhoneRules.InvalidInternationalPhoneMessage)
            .When(x => x.PhoneNumber is not null);

        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Email là bắt buộc.")
            .MaximumLength(255)
            .WithMessage("Email không được vượt quá 255 ký tự.")
            .EmailAddress()
            .WithMessage("Email không đúng định dạng.")
            .Must(EmailRules.HasAllowedRegistrationDomain)
            .WithMessage(EmailRules.AllowedEmailDomainMessage)
            .When(x => x.Email is not null);

        RuleFor(x => x.RoleId)
            .GreaterThan(0)
            .WithMessage("Vai trò là bắt buộc.")
            .When(x => x.RoleId.HasValue);

    }
}

public sealed class UpdateUserRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly IUserCodeGenerator _userCodeGenerator;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateUserRequestUseCase(
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

    public async Task<AuthUserDto> ExecuteAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);
        var user = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == request.UserId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy user.");

        UserManagementSupport.EnsureCanUpdateUser(actor, user);
        var oldValues = UserAuditSupport.CreateUserSnapshot(user);

        var targetRole = request.RoleId.HasValue
            ? await AuthSupport.GetRoleByIdAsync(_context, request.RoleId.Value, nameof(request.RoleId), cancellationToken)
            : user.Role;
        var roleChanged = targetRole.Id != user.RoleId;

        if (roleChanged)
        {
            UserManagementSupport.EnsureCanAssignRole(actor, user, targetRole, nameof(request.RoleId));
        }

        if (request.Email is not null)
        {
            var normalizedEmail = _identityNormalizer.NormalizeEmail(request.Email);
            if (normalizedEmail != user.NormalizedEmail
                && await _context.Set<ExternalLogin>().AnyAsync(
                    x => x.UserId == user.Id && x.Provider == AuthSupport.GoogleProvider,
                    cancellationToken))
            {
                throw AuthSupport.CreateValidationException(
                    nameof(request.Email),
                    "Tài khoản đăng nhập Google không được đổi email.");
            }

            if (await _context.Set<User>().AnyAsync(x => x.NormalizedEmail == normalizedEmail && x.Id != user.Id, cancellationToken))
            {
                throw AuthSupport.CreateValidationException(nameof(request.Email), "Email đã được đăng ký.");
            }

            user.Email = request.Email.Trim();
            user.NormalizedEmail = normalizedEmail;
        }

        if (request.PhoneNumber is not null)
        {
            var effectiveEmail = request.Email is null ? user.Email : request.Email;
            if (!PhoneRules.IsVietnamPhone(request.PhoneNumber)
                && !EmailRules.HasAllowedRegistrationDomain(effectiveEmail))
            {
                throw AuthSupport.CreateValidationException(
                    nameof(request.PhoneNumber),
                    "Số điện thoại quốc tế bắt buộc tài khoản có email được hỗ trợ.");
            }

            var normalizedPhone = _identityNormalizer.NormalizePhone(request.PhoneNumber);
            var phoneChanged = normalizedPhone != user.NormalizedPhoneNumber;

            if (await _context.Set<User>().AnyAsync(
                    x => x.NormalizedPhoneNumber == normalizedPhone && x.Id != user.Id,
                    cancellationToken))
            {
                throw AuthSupport.CreateValidationException(nameof(request.PhoneNumber), "Số điện thoại đã được đăng ký.");
            }

            user.PhoneNumber = PhoneRules.ToInternationalFormat(request.PhoneNumber);
            user.NormalizedPhoneNumber = normalizedPhone;
            if (user.Status == UserStatus.Active && (phoneChanged || user.PhoneVerifiedAt is null))
            {
                user.PhoneVerifiedAt = _timeProvider.GetUtcNow();
            }
        }

        if (request.FullName is not null)
        {
            user.FullName = request.FullName.Trim();
        }

        if (request.DateOfBirth.HasValue)
        {
            user.DateOfBirth = request.DateOfBirth;
        }

        if (roleChanged)
        {
            user.RoleId = targetRole.Id;

            if (!UserCodes.HasPrefix(user.UserCode, UserCodes.GetPrefixForRoleCode(targetRole.Code)))
            {
                user.UserCode = await _userCodeGenerator.GenerateNextCodeAsync(targetRole.Code, cancellationToken);
            }
        }

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, _timeProvider.GetUtcNow(), cancellationToken);
        _context.AuditLogs.Add(UserAuditSupport.CreateUserAuditLog(
            actor.Id,
            AuditActions.UpdateUser,
            user.Id,
            oldValues,
            UserAuditSupport.CreateUserSnapshot(user, targetRole),
            _timeProvider.GetUtcNow()));
        await _context.SaveChangesAsync(cancellationToken);

        var updatedUser = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleAsync(x => x.Id == user.Id, cancellationToken);

        return AuthSupport.CreateUserDto(updatedUser);
    }

}
