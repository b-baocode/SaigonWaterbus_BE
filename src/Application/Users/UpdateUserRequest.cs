using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Users;

public sealed record UpdateUserRequest(
    Guid UserId,
    string? FullName,
    DateOnly? DateOfBirth,
    string? PhoneNumber,
    string? Email,
    Guid? RoleId,
    string? Gender = null,
    string? Nationality = null,
    StaffType? StaffType = null);

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
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

        RuleFor(x => x.Gender)
            .MaximumLength(30)
            .WithMessage("Giới tính không được vượt quá 30 ký tự.");

        RuleFor(x => x.Nationality)
            .MaximumLength(100)
            .WithMessage("Quốc tịch không được vượt quá 100 ký tự.");

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
            .NotEmpty()
            .WithMessage("Vai trò là bắt buộc.")
            .When(x => x.RoleId.HasValue);

        RuleFor(x => x.StaffType)
            .IsInEnum()
            .When(x => x.StaffType.HasValue);
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

        var targetRole = request.RoleId.HasValue
            ? await AuthSupport.GetRoleByIdAsync(_context, request.RoleId.Value, nameof(request.RoleId), cancellationToken)
            : user.Role;
        var roleChanged = targetRole.Id != user.RoleId;
        var targetStaffType = targetRole.SystemName == Roles.StaffSystemName
            ? request.StaffType ?? user.StaffType
            : null;

        if (roleChanged)
        {
            UserManagementSupport.EnsureCanAssignRole(actor, user, targetRole, nameof(request.RoleId));
        }

        if (request.StaffType.HasValue || roleChanged)
        {
            if (targetRole.SystemName != Roles.StaffSystemName && request.StaffType.HasValue)
            {
                UserManagementSupport.EnsureValidStaffTypeForRole(
                    targetRole,
                    request.StaffType,
                    nameof(request.StaffType));
            }

            UserManagementSupport.EnsureValidStaffTypeForRole(
                targetRole,
                targetStaffType,
                nameof(request.StaffType));

            if (AuthSupport.IsManager(actor) && targetStaffType is not StaffType.Ground)
            {
                throw AuthSupport.CreateValidationException(
                    nameof(request.StaffType),
                    "Manager chỉ được cập nhật nhân viên mặt đất.");
            }
        }

        if (targetStaffType == StaffType.OnBoard
            && await _context.Set<UserStationAssignment>()
                .AnyAsync(x => x.UserId == user.Id && x.IsActive, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.StaffType),
                "Vui lòng bỏ gắn bến trước khi chuyển nhân viên sang loại trên tàu.");
        }

        if (request.Email is not null)
        {
            var normalizedEmail = _identityNormalizer.NormalizeEmail(request.Email);
            if (await AuthSupport.WhereUserIdentityMatches(_context.Set<User>(), null, normalizedEmail)
                    .AnyAsync(x => x.Id != user.Id, cancellationToken))
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
            var currentNormalizedPhone = user.PhoneNumber is null ? null : _identityNormalizer.NormalizePhone(user.PhoneNumber);
            var phoneChanged = normalizedPhone != currentNormalizedPhone;

            if (await AuthSupport.WhereUserIdentityMatches(_context.Set<User>(), normalizedPhone, null)
                    .AnyAsync(x => x.Id != user.Id, cancellationToken))
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

        if (request.Gender is not null)
        {
            user.Gender = AuthSupport.NormalizeOptionalText(request.Gender);
        }

        if (request.Nationality is not null)
        {
            user.Nationality = AuthSupport.NormalizeOptionalText(request.Nationality);
        }

        if (roleChanged)
        {
            user.RoleId = targetRole.Id;

            if (!UserCodes.HasPrefix(user.UserCode, UserCodes.GetPrefixForRoleCode(targetRole.Code)))
            {
                user.UserCode = await _userCodeGenerator.GenerateNextCodeAsync(targetRole.Code, cancellationToken);
            }
        }

        if (request.StaffType.HasValue || roleChanged)
        {
            user.StaffType = targetStaffType;
        }

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, _timeProvider.GetUtcNow(), cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        var updatedUser = await _context.Set<User>()
            .Include(x => x.Role)
            .Include(x => x.StationAssignments).ThenInclude(a => a.Station)
            .SingleAsync(x => x.Id == user.Id, cancellationToken);

        return AuthSupport.CreateUserDto(updatedUser);
    }

}
