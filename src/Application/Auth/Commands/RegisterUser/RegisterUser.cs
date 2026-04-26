using Microsoft.EntityFrameworkCore.Storage;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Commands.RegisterUser;

public record RegisterUserCommand(
    string UserName,
    string Password,
    string FullName,
    string Email,
    string? PhoneNumber) : IRequest<RegisterUserResult>;

public record RegisterUserResult(
    int UserId,
    string UserName,
    string Email,
    string Role,
    string Message);

public class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    private static readonly string AllowedDomainsMessage =
        $"Email must use one of these domains: {string.Join(", ", EmailRules.AllowedRegistrationDomains)}.";

    public RegisterUserCommandValidator(IApplicationDbContext context)
    {
        RuleFor(x => x.UserName)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(50)
            .Must(x => x.Trim().Length >= 3)
            .WithMessage("UserName must be at least 3 characters after trimming spaces.")
            .MustAsync(async (userName, cancellationToken) =>
            {
                var normalizedUserName = userName.Trim();
                return !await context.Users.AnyAsync(x => x.UserName == normalizedUserName, cancellationToken);
            })
            .WithMessage("UserName already exists.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(100);

        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(150)
            .Must(EmailRules.HasAllowedRegistrationDomain)
            .WithMessage(AllowedDomainsMessage)
            .MustAsync(async (email, cancellationToken) =>
            {
                var normalizedEmail = email.Trim().ToLowerInvariant();
                return !await context.Users.AnyAsync(x => x.Email == normalizedEmail, cancellationToken);
            })
            .WithMessage("Email already exists.");

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(20);
    }
}

public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, RegisterUserResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRegistrationNotificationService _registrationNotificationService;

    public RegisterUserCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IRegistrationNotificationService registrationNotificationService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _registrationNotificationService = registrationNotificationService;
    }

    public async Task<RegisterUserResult> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var normalizedUserName = request.UserName.Trim();
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var normalizedFullName = request.FullName.Trim();
        var normalizedPhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : request.PhoneNumber.Trim();

        var defaultRole = await _context.Roles
            .SingleOrDefaultAsync(x => x.Code == RoleRules.DefaultRegistrationRoleCode, cancellationToken);

        if (defaultRole is null)
        {
            throw new InvalidOperationException(
                $"Default registration role '{RoleRules.DefaultRegistrationRoleCode}' was not found.");
        }

        await using IDbContextTransaction transaction = await _context.BeginTransactionAsync(cancellationToken);

        var user = new User
        {
            UserName = normalizedUserName,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FullName = normalizedFullName,
            Email = normalizedEmail,
            PhoneNumber = normalizedPhoneNumber,
            RoleId = defaultRole.Id,
            IsActive = true
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        await _registrationNotificationService.SendRegistrationCreatedAsync(
            user.Email,
            user.FullName,
            defaultRole.Name,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new RegisterUserResult(
            user.Id,
            user.UserName,
            user.Email,
            defaultRole.Code,
            "Registration completed. A notification email was sent.");
    }
}
