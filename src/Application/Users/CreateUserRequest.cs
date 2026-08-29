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
    string? Nationality = null,
    StaffType? StaffType = null,
    IReadOnlyCollection<Guid>? StationIds = null,
    Guid? PrimaryStationId = null);

public sealed record ManagedUserPasswordResultDto(
    AuthUserDto User,
    string GeneratedPassword);

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FullName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Họ và tên không được để trống.")
            .MaximumLength(150)
            .WithMessage("Họ và tên không được vượt quá 150 ký tự.")
            .Must(UserInputValidationSupport.IsValidFullName)
            .WithMessage(UserInputValidationSupport.InvalidFullNameMessage);

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
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Số điện thoại là bắt buộc.")
            .Must(PhoneRules.IsValid)
            .WithMessage(PhoneRules.InvalidInternationalPhoneMessage);

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

        RuleFor(x => x.StaffType)
            .IsInEnum()
            .When(x => x.StaffType.HasValue);

        RuleFor(x => x.StationIds)
            .Must(ids => ids is null || ids.All(id => id != Guid.Empty))
            .WithMessage("StationId không hợp lệ.");

        RuleFor(x => x.StationIds)
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Danh sách bến không được trùng.");

        RuleFor(x => x)
            .Must(x => !x.PrimaryStationId.HasValue
                || x.StationIds is not null && x.StationIds.Contains(x.PrimaryStationId.Value))
            .WithMessage("PrimaryStationId phải nằm trong danh sách stationIds.")
            .OverridePropertyName(nameof(CreateUserRequest.PrimaryStationId));
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

        UserManagementSupport.EnsureCanCreateRole(actor, role, request.StaffType, nameof(request.RoleId));
        UserManagementSupport.EnsureValidStaffTypeForRole(role, request.StaffType, nameof(request.StaffType));

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
        var stationIds = UserManagementSupport.NormalizeStationIds(request.StationIds);
        await EnsureCanCreateWithStationsAsync(
            actor,
            role,
            request.StaffType,
            stationIds,
            cancellationToken);
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
            StaffType = request.StaffType,
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

            if (stationIds.Count > 0)
            {
                var primaryStationId = request.PrimaryStationId ?? stationIds[0];
                foreach (var stationId in stationIds)
                {
                    _context.Set<UserStationAssignment>().Add(new UserStationAssignment
                    {
                        UserId = user.Id,
                        StationId = stationId,
                        IsPrimary = stationId == primaryStationId,
                        IsActive = true,
                        AssignedAt = now,
                        AssignedByUserId = actor.Id
                    });
                }

                await _context.SaveChangesAsync(ct);
            }
        }, cancellationToken);

        var createdUser = await _context.Set<User>()
            .Include(x => x.Role)
            .Include(x => x.StationAssignments).ThenInclude(a => a.Station)
            .SingleAsync(x => x.Id == user.Id, cancellationToken);

        return new ManagedUserPasswordResultDto(AuthSupport.CreateUserDto(createdUser), generatedPassword);
    }

    private async Task EnsureCanCreateWithStationsAsync(
        User actor,
        Role targetRole,
        StaffType? staffType,
        IReadOnlyList<Guid> stationIds,
        CancellationToken cancellationToken)
    {
        if (staffType == StaffType.OnBoard && stationIds.Count > 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(CreateUserRequest.StationIds),
                "Nhân viên trên tàu không gắn trực tiếp vào bến.");
        }

        if (AuthSupport.IsManager(actor) && staffType == StaffType.Ground && stationIds.Count == 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(CreateUserRequest.StationIds),
                "Manager cần chọn bến khi tạo nhân viên mặt đất.");
        }

        if (stationIds.Count == 0)
        {
            return;
        }

        if (!AuthSupport.IsManager(actor)
            && targetRole.SystemName is not (Roles.ManagerSystemName or Roles.StaffSystemName))
        {
            throw AuthSupport.CreateValidationException(
                nameof(CreateUserRequest.StationIds),
                "Chỉ được gắn bến cho tài khoản Manager hoặc Staff.");
        }

        var existingStationIds = await _context.Set<Station>()
            .Where(x => stationIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var missing = stationIds.Except(existingStationIds).ToList();
        if (missing.Count > 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(CreateUserRequest.StationIds),
                "Có bến không tồn tại.");
        }

        if (!AuthSupport.IsManager(actor))
        {
            return;
        }

        var managerStationIds = await _context.Set<UserStationAssignment>()
            .Where(x => x.UserId == actor.Id && x.IsActive)
            .Select(x => x.StationId)
            .ToListAsync(cancellationToken);
        var managerStationSet = managerStationIds.ToHashSet();
        if (managerStationSet.Count == 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(CreateUserRequest.StationIds),
                "Manager chưa được gắn bến nên không thể tạo nhân viên mặt đất.");
        }

        if (stationIds.Any(stationId => !managerStationSet.Contains(stationId)))
        {
            throw AuthSupport.CreateValidationException(
                nameof(CreateUserRequest.StationIds),
                "Manager chỉ được tạo nhân viên mặt đất trong các bến mình phụ trách.");
        }
    }
}
