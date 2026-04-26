using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
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
            .MaximumLength(20);

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
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        var normalizedPhone = _identityNormalizer.NormalizePhone(request.Phone);
        var normalizedEmail = _identityNormalizer.NormalizeEmail(request.Email);
        var now = _timeProvider.GetUtcNow();

        if (await AuthSupport.RemoveExpiredPendingRegistrationUsersByIdentityAsync(
                _context,
                normalizedPhone,
                normalizedEmail,
                now,
                cancellationToken))
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        var customerRole = await AuthSupport.GetRoleByCodeAsync(
            _context,
            Domain.Constants.Roles.CustomerCode,
            cancellationToken);

        if (await _context.Set<User>().AnyAsync(x => x.NormalizedPhoneNumber == normalizedPhone, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Phone), "Phone number is already registered.");
        }

        if (await _context.Set<User>().AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Email), "Email is already registered.");
        }

        var email = request.Email.Trim();
        var otpCode = _otpCodeService.GenerateCode();

        var user = new User
        {
            FullName = request.FullName.Trim(),
            DateOfBirth = request.DateOfBirth,
            PhoneNumber = request.Phone.Trim(),
            NormalizedPhoneNumber = normalizedPhone,
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = _secretHasher.Hash(request.Password),
            RoleId = customerRole.Id,
            Status = UserStatus.PendingVerification
        };

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

        _context.Set<User>().Add(user);
        _context.Set<OtpChallenge>().Add(challenge);

        await _context.SaveChangesAsync(cancellationToken);
        await _otpSender.SendAsync(email, otpCode, OtpPurpose.Register, user.FullName, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new OtpChallengeDto(
            challenge.Id,
            _otpCodeService.MaskEmail(email),
            challenge.ExpiresAt,
            challenge.ResendAvailableAt);
    }
}
