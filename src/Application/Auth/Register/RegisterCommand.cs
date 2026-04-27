using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Register;

public sealed record RegisterCommand(
    string FullName,
    DateOnly DateOfBirth,
    string Phone,
    string Email,
    string Password) : IRequest<OtpChallengeDto>;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.DateOfBirth)
            .Must(x => x <= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("Date of birth cannot be in the future.");

        RuleFor(x => x.Phone)
            .NotEmpty()
            .Must(PhoneRules.IsValid)
            .WithMessage("Phone number must contain exactly 10 digits.");

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8);

    }
}

public sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, OtpChallengeDto>
{
    private sealed record PendingRegistrationOtp(
        int ChallengeId,
        string Email,
        string FullName,
        string Code,
        DateTimeOffset ExpiresAt,
        DateTimeOffset ResendAvailableAt);

    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCodeService _otpCodeService;
    private readonly IOtpSender _otpSender;
    private readonly IOtpPolicy _otpPolicy;
    private readonly TimeProvider _timeProvider;

    public RegisterCommandHandler(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        IOtpCodeService otpCodeService,
        IOtpSender otpSender,
        IOtpPolicy otpPolicy,
        TimeProvider timeProvider)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _otpCodeService = otpCodeService;
        _otpSender = otpSender;
        _otpPolicy = otpPolicy;
        _timeProvider = timeProvider;
    }

    public async Task<OtpChallengeDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var normalizedPhone = _identityNormalizer.NormalizePhone(request.Phone);
        var normalizedEmail = _identityNormalizer.NormalizeEmail(request.Email);
        var now = _timeProvider.GetUtcNow();

        var pendingRegistration = await _context.ExecuteInTransactionAsync(async ct =>
        {
            if (await AuthSupport.RemoveExpiredPendingRegistrationUsersByIdentityAsync(
                    _context,
                    normalizedPhone,
                    normalizedEmail,
                    now,
                    ct))
            {
                await _context.SaveChangesAsync(ct);
            }

            var customerRole = await AuthSupport.GetRoleByCodeAsync(
                _context,
                Domain.Constants.Roles.CustomerCode,
                ct);

            var matchingUsers = await _context.Set<User>()
                .Include(x => x.OtpChallenges)
                .Where(x => x.NormalizedPhoneNumber == normalizedPhone || x.NormalizedEmail == normalizedEmail)
                .ToListAsync(ct);

            var pendingUser = matchingUsers.SingleOrDefault(x =>
                x.Status == UserStatus.PendingVerification
                && x.NormalizedPhoneNumber == normalizedPhone
                && x.NormalizedEmail == normalizedEmail);

            foreach (var matchingUser in matchingUsers)
            {
                if (pendingUser is not null && matchingUser.Id == pendingUser.Id)
                {
                    continue;
                }

                if (matchingUser.NormalizedPhoneNumber == normalizedPhone)
                {
                    throw AuthSupport.CreateValidationException(nameof(request.Phone), "Phone number is already registered.");
                }

                if (matchingUser.NormalizedEmail == normalizedEmail)
                {
                    throw AuthSupport.CreateValidationException(nameof(request.Email), "Email is already registered.");
                }
            }

            if (pendingUser is not null)
            {
                var latestPendingChallenge = pendingUser.OtpChallenges
                    .Where(x => x.Purpose == OtpPurpose.Register && x.ConsumedAt == null)
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefault();

                if (latestPendingChallenge is not null && latestPendingChallenge.ResendAvailableAt > now)
                {
                    throw AuthSupport.CreateValidationException(nameof(request.Email), "OTP was sent recently. Please wait before requesting again.");
                }
            }

            var email = request.Email.Trim();
            var otpCode = _otpCodeService.GenerateCode();

            var user = pendingUser ?? new User
            {
                Status = UserStatus.PendingVerification
            };

            user.FullName = request.FullName.Trim();
            user.DateOfBirth = request.DateOfBirth;
            user.PhoneNumber = request.Phone.Trim();
            user.NormalizedPhoneNumber = normalizedPhone;
            user.Email = email;
            user.NormalizedEmail = normalizedEmail;
            user.PasswordHash = _secretHasher.Hash(request.Password);
            user.RoleId = customerRole.Id;
            user.Status = UserStatus.PendingVerification;
            user.EmailVerifiedAt = null;

            if (pendingUser is null)
            {
                _context.Set<User>().Add(user);
            }
            else
            {
                await AuthSupport.RetirePendingOtpChallengesAsync(_context, user.Id, OtpPurpose.Register, now, ct);
            }

            var challenge = new OtpChallenge
            {
                User = user,
                Purpose = OtpPurpose.Register,
                Email = email,
                CodeHash = _secretHasher.Hash(otpCode),
                ExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
                ResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
                MaxAttempts = _otpPolicy.MaxAttempts
            };

            _context.Set<OtpChallenge>().Add(challenge);

            await _context.SaveChangesAsync(ct);
            return new PendingRegistrationOtp(
                challenge.Id,
                email,
                user.FullName,
                otpCode,
                challenge.ExpiresAt,
                challenge.ResendAvailableAt);
        }, cancellationToken);

        await _otpSender.SendAsync(
            pendingRegistration.Email,
            pendingRegistration.Code,
            OtpPurpose.Register,
            pendingRegistration.FullName,
            cancellationToken);

        return new OtpChallengeDto(
            pendingRegistration.ChallengeId,
            _otpCodeService.MaskEmail(pendingRegistration.Email),
            pendingRegistration.ExpiresAt,
            pendingRegistration.ResendAvailableAt);
    }
}
