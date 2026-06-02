using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Password;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty()
            .WithMessage("Mật khẩu hiện tại là bắt buộc.");

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .WithMessage("Mật khẩu mới là bắt buộc.")
            .Must(PasswordRules.IsStrong)
            .WithMessage(PasswordRules.StrongPasswordMessage)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("Mật khẩu mới phải khác mật khẩu hiện tại.");
    }
}

public sealed class ChangePasswordRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ChangePasswordRequestUseCase(
        IApplicationDbContext context,
        ISecretHasher secretHasher,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _secretHasher = secretHasher;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<AuthActionResultDto> ExecuteAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (!_userContext.UserId.HasValue)
        {
            throw new UnauthorizedAccessException();
        }

        var user = await _context.Set<User>()
            .SingleOrDefaultAsync(x => x.Id == _userContext.UserId.Value, cancellationToken)
            ?? throw new global::SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy user.");

        AuthSupport.EnsureUserCanLogin(user);

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            throw AuthSupport.CreateValidationException(nameof(request.CurrentPassword), "Tài khoản này chưa hỗ trợ đăng nhập bằng mật khẩu.");
        }

        if (!_secretHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw AuthSupport.CreateValidationException(nameof(request.CurrentPassword), "Mật khẩu hiện tại không đúng.");
        }

        if (_secretHasher.Verify(request.NewPassword, user.PasswordHash))
        {
            throw AuthSupport.CreateValidationException(nameof(request.NewPassword), "Mật khẩu mới phải khác mật khẩu hiện tại.");
        }

        var now = _timeProvider.GetUtcNow();
        user.PasswordHash = _secretHasher.Hash(request.NewPassword);

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Doi mat khau thanh cong.");
    }
}
