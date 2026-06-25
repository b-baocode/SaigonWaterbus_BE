using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Auth.Login;
using SaigonWaterbus.Application.Auth.Otp;
using SaigonWaterbus.Application.Auth.Password;
using SaigonWaterbus.Application.Auth.Profile;
using SaigonWaterbus.Application.Auth.Register;
using SaigonWaterbus.Application.Auth.Token;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IRequestValidator _validator;
    private readonly RegisterRequestUseCase _register;
    private readonly VerifyRegisterOtpRequestUseCase _verifyRegisterOtp;
    private readonly ResendOtpRequestUseCase _resendOtp;
    private readonly LoginRequestUseCase _login;
    private readonly GoogleLoginRequestUseCase _googleLogin;
    private readonly RefreshTokenRequestUseCase _refreshToken;
    private readonly LogoutRequestUseCase _logout;
    private readonly GetCurrentUserProfileRequestUseCase _getCurrentUserProfile;
    private readonly UpdateCurrentUserProfileRequestUseCase _updateCurrentUserProfile;
    private readonly DeleteCurrentUserAccountRequestUseCase _deleteCurrentUserAccount;
    private readonly VerifyEmailChangeOtpRequestUseCase _verifyEmailChangeOtp;
    private readonly VerifyPhoneChangeOtpRequestUseCase _verifyPhoneChangeOtp;
    private readonly ForgotPasswordOtpRequestUseCase _forgotPassword;
    private readonly ResetPasswordRequestUseCase _resetPassword;
    private readonly ChangePasswordRequestUseCase _changePassword;

    public AuthService(
        IRequestValidator validator,
        RegisterRequestUseCase register,
        VerifyRegisterOtpRequestUseCase verifyRegisterOtp,
        ResendOtpRequestUseCase resendOtp,
        LoginRequestUseCase login,
        GoogleLoginRequestUseCase googleLogin,
        RefreshTokenRequestUseCase refreshToken,
        LogoutRequestUseCase logout,
        GetCurrentUserProfileRequestUseCase getCurrentUserProfile,
        UpdateCurrentUserProfileRequestUseCase updateCurrentUserProfile,
        DeleteCurrentUserAccountRequestUseCase deleteCurrentUserAccount,
        VerifyEmailChangeOtpRequestUseCase verifyEmailChangeOtp,
        VerifyPhoneChangeOtpRequestUseCase verifyPhoneChangeOtp,
        ForgotPasswordOtpRequestUseCase forgotPassword,
        ResetPasswordRequestUseCase resetPassword,
        ChangePasswordRequestUseCase changePassword)
    {
        _validator = validator;
        _register = register;
        _verifyRegisterOtp = verifyRegisterOtp;
        _resendOtp = resendOtp;
        _login = login;
        _googleLogin = googleLogin;
        _refreshToken = refreshToken;
        _logout = logout;
        _getCurrentUserProfile = getCurrentUserProfile;
        _updateCurrentUserProfile = updateCurrentUserProfile;
        _deleteCurrentUserAccount = deleteCurrentUserAccount;
        _verifyEmailChangeOtp = verifyEmailChangeOtp;
        _verifyPhoneChangeOtp = verifyPhoneChangeOtp;
        _forgotPassword = forgotPassword;
        _resetPassword = resetPassword;
        _changePassword = changePassword;
    }

    public async Task<OtpChallengeDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _register.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthActionResultDto> VerifyRegisterOtpAsync(
        VerifyRegisterOtpRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _verifyRegisterOtp.ExecuteAsync(request, cancellationToken);
    }

    public async Task<OtpChallengeDto> ResendOtpAsync(ResendOtpRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _resendOtp.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthSessionDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _login.ExecuteAsync(request, cancellationToken);
    }

    public async Task<GoogleLoginResultDto> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _googleLogin.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthSessionDto> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _refreshToken.ExecuteAsync(request, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        await _logout.ExecuteAsync(new LogoutRequest(), cancellationToken);
    }

    public async Task<AuthUserDto> GetCurrentUserProfileAsync(CancellationToken cancellationToken) =>
        await _getCurrentUserProfile.ExecuteAsync(new GetCurrentUserProfileRequest(), cancellationToken);

    public async Task<UpdateProfileResultDto> UpdateCurrentUserProfileAsync(
        UpdateCurrentUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateCurrentUserProfile.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthActionResultDto> DeleteCurrentUserAccountAsync(CancellationToken cancellationToken) =>
        await _deleteCurrentUserAccount.ExecuteAsync(new DeleteCurrentUserAccountRequest(), cancellationToken);

    public async Task<AuthUserDto> VerifyEmailChangeOtpAsync(
        VerifyEmailChangeOtpRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _verifyEmailChangeOtp.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthUserDto> VerifyPhoneChangeOtpAsync(
        VerifyPhoneChangeOtpRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _verifyPhoneChangeOtp.ExecuteAsync(request, cancellationToken);
    }

    public async Task<OtpChallengeDto> ForgotPasswordAsync(
        ForgotPasswordOtpRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _forgotPassword.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthActionResultDto> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _resetPassword.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthActionResultDto> ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _changePassword.ExecuteAsync(request, cancellationToken);
    }
}
