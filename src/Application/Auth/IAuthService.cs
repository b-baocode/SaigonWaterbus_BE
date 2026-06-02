using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Auth.Login;
using SaigonWaterbus.Application.Auth.Otp;
using SaigonWaterbus.Application.Auth.Password;
using SaigonWaterbus.Application.Auth.Profile;
using SaigonWaterbus.Application.Auth.Register;
using SaigonWaterbus.Application.Auth.Token;

namespace SaigonWaterbus.Application.Auth;

public interface IAuthService
{
    Task<OtpChallengeDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthActionResultDto> VerifyRegisterOtpAsync(VerifyRegisterOtpRequest request, CancellationToken cancellationToken);

    Task<OtpChallengeDto> ResendOtpAsync(ResendOtpRequest request, CancellationToken cancellationToken);

    Task<AuthSessionDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<GoogleLoginResultDto> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken cancellationToken);

    Task<GooglePhoneOtpSentDto> SendGooglePhoneOtpAsync(SendGooglePhoneOtpRequest request, CancellationToken cancellationToken);

    Task<AuthSessionDto> VerifyGooglePhoneAsync(VerifyGooglePhoneRequest request, CancellationToken cancellationToken);

    Task<AuthSessionDto> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);

    Task<AuthUserDto> GetCurrentUserProfileAsync(CancellationToken cancellationToken);

    Task<UpdateProfileResultDto> UpdateCurrentUserProfileAsync(UpdateCurrentUserProfileRequest request, CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteCurrentUserAccountAsync(CancellationToken cancellationToken);

    Task<AuthUserDto> VerifyEmailChangeOtpAsync(VerifyEmailChangeOtpRequest request, CancellationToken cancellationToken);

    Task<OtpChallengeDto> ForgotPasswordAsync(ForgotPasswordOtpRequest request, CancellationToken cancellationToken);

    Task<AuthActionResultDto> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);

    Task<AuthActionResultDto> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken);
}
