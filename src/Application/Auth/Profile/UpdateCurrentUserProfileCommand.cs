using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Profile;

public sealed record UpdateCurrentUserProfileCommand(
    string? FullName = null,
    DateOnly? DateOfBirth = null,
    string? Email = null) : IRequest<UpdateProfileResultDto>;

public sealed class UpdateCurrentUserProfileCommandValidator : AbstractValidator<UpdateCurrentUserProfileCommand>
{
    public UpdateCurrentUserProfileCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(150)
            .When(x => x.FullName is not null);

        RuleFor(x => x.DateOfBirth)
            .Must(x => !x.HasValue || x.Value <= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("Date of birth cannot be in the future.");

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .When(x => x.Email is not null);
    }
}

public sealed class UpdateCurrentUserProfileCommandHandler : IRequestHandler<UpdateCurrentUserProfileCommand, UpdateProfileResultDto>
{
    private sealed record PendingEmailVerification(
        int ChallengeId,
        string Email,
        string Code,
        DateTimeOffset ExpiresAt,
        DateTimeOffset ResendAvailableAt);

    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCodeService _otpCodeService;
    private readonly IOtpSender _otpSender;
    private readonly IOtpPolicy _otpPolicy;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateCurrentUserProfileCommandHandler(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        IOtpCodeService otpCodeService,
        IOtpSender otpSender,
        IOtpPolicy otpPolicy,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _otpCodeService = otpCodeService;
        _otpSender = otpSender;
        _otpPolicy = otpPolicy;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<UpdateProfileResultDto> Handle(UpdateCurrentUserProfileCommand request, CancellationToken cancellationToken)
    {
        var user = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        AuthSupport.EnsureUserCanLogin(user);

        var hasEmailUpdate = request.Email is not null;
        var email = hasEmailUpdate ? request.Email!.Trim() : user.Email;
        var normalizedEmail = hasEmailUpdate ? _identityNormalizer.NormalizeEmail(email!) : user.NormalizedEmail;
        var emailChanged = hasEmailUpdate && normalizedEmail != user.NormalizedEmail;
        var now = _timeProvider.GetUtcNow();

        PendingEmailVerification? pendingEmailVerification = null;

        await _context.ExecuteInTransactionAsync(async ct =>
        {
            OtpChallenge? challenge = null;
            string? otpCode = null;

            if (emailChanged)
            {
                if (await _context.Set<User>().AnyAsync(x => x.NormalizedEmail == normalizedEmail && x.Id != user.Id, ct))
                {
                    throw AuthSupport.CreateValidationException(nameof(request.Email), "Email is already registered.");
                }

                var latestPendingChallenge = await _context.Set<OtpChallenge>()
                    .Where(x => x.UserId == user.Id
                             && x.Purpose == OtpPurpose.EmailChange
                             && x.ConsumedAt == null)
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefaultAsync(ct);

                if (latestPendingChallenge is not null
                    && _identityNormalizer.NormalizeEmail(latestPendingChallenge.Email) == normalizedEmail
                    && latestPendingChallenge.ResendAvailableAt > now)
                {
                    throw AuthSupport.CreateValidationException(nameof(request.Email), "OTP was sent recently. Please wait before requesting again.");
                }

                await AuthSupport.RetirePendingOtpChallengesAsync(_context, user.Id, OtpPurpose.EmailChange, now, ct);

                otpCode = _otpCodeService.GenerateCode();
                challenge = new OtpChallenge
                {
                    UserId = user.Id,
                    Purpose = OtpPurpose.EmailChange,
                    Email = email!,
                    CodeHash = _secretHasher.Hash(otpCode),
                    ExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
                    ResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
                    MaxAttempts = _otpPolicy.MaxAttempts
                };

                _context.Set<OtpChallenge>().Add(challenge);
            }
            else if (hasEmailUpdate)
            {
                user.Email = email;
                user.NormalizedEmail = normalizedEmail;
            }

            if (request.FullName is not null)
            {
                user.FullName = request.FullName.Trim();
            }

            if (request.DateOfBirth.HasValue)
            {
                user.DateOfBirth = request.DateOfBirth;
            }

            await _context.SaveChangesAsync(ct);

            if (challenge is not null && otpCode is not null)
            {
                pendingEmailVerification = new PendingEmailVerification(
                    challenge.Id,
                    email!,
                    otpCode,
                    challenge.ExpiresAt,
                    challenge.ResendAvailableAt);
            }
        }, cancellationToken);

        if (pendingEmailVerification is not null)
        {
            await _otpSender.SendAsync(
                pendingEmailVerification.Email,
                pendingEmailVerification.Code,
                OtpPurpose.EmailChange,
                user.FullName,
                cancellationToken);
        }

        var updatedUser = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleAsync(x => x.Id == user.Id, cancellationToken);

        return new UpdateProfileResultDto(
            AuthSupport.CreateUserDto(updatedUser),
            pendingEmailVerification is null
                ? null
                : new OtpChallengeDto(
                    pendingEmailVerification.ChallengeId,
                    _otpCodeService.MaskEmail(pendingEmailVerification.Email),
                    pendingEmailVerification.ExpiresAt,
                    pendingEmailVerification.ResendAvailableAt));
    }
}
