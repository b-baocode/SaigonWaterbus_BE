using SaigonWaterbus.Application.Auth.Login;
using SaigonWaterbus.Application.Auth.Otp;
using SaigonWaterbus.Application.Auth.Password;
using SaigonWaterbus.Application.Auth.Profile;
using SaigonWaterbus.Application.Auth.Register;
using SaigonWaterbus.Application.Auth.Token;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Auth : IEndpointGroup
{
    public static string RoutePrefix => "/api/auth";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(Register, "register")
            .AllowAnonymous()
            .WithSummary("Dang ky tai khoan")
            .WithDescription(
                "Quyen truy cap: Anonymous (khong can token).\n" +
                "API Note:\n" +
                "- Body: fullName, dateOfBirth, phone, email, password.\n" +
                "- Tao user o trang thai PendingVerification va tra ve challengeId.\n" +
                "- Sau khi goi endpoint nay, tiep tuc goi /api/auth/register/verify-otp.");

        groupBuilder.MapPost(VerifyRegisterOtp, "register/verify-otp")
            .AllowAnonymous()
            .WithSummary("Xac nhan OTP dang ky")
            .WithDescription(
                "Quyen truy cap: Anonymous (khong can token).\n" +
                "API Note:\n" +
                "- Body: challengeId, code.\n" +
                "- Dung challengeId tra ve tu /api/auth/register.\n" +
                "- Thanh cong se kich hoat tai khoan.");

        groupBuilder.MapPost(ResendOtp, "resend-otp")
            .AllowAnonymous()
            .WithSummary("Gui lai OTP")
            .WithDescription(
                "Quyen truy cap: Anonymous (khong can token).\n" +
                "API Note:\n" +
                "- Body: challengeId.\n" +
                "- Dung cho challenge dang ky hoac quen mat khau.\n" +
                "- Tra ve challengeId moi, expiresAt moi, resendAvailableAt moi.");

        groupBuilder.MapPost(Login, "login")
            .AllowAnonymous()
            .WithSummary("Dang nhap bang email va mat khau")
            .WithDescription(
                "Quyen truy cap: Anonymous (khong can token).\n" +
                "API Note:\n" +
                "- Body: email, password.\n" +
                "- Tra ve user + accessToken + refreshToken.\n" +
                "- Dung accessToken cho cac endpoint can Bearer token.");

        groupBuilder.MapPost(GoogleLogin, "login/google")
            .AllowAnonymous()
            .WithSummary("Dang nhap bang Google")
            .WithDescription(
                "Quyen truy cap: Anonymous (khong can token).\n" +
                "API Note:\n" +
                "- Body: idToken.\n" +
                "- Backend se verify Google token, tao/link user neu can.\n" +
                "- Tra ve user + accessToken + refreshToken.");

        groupBuilder.MapPost(ForgotPasswordRequestOtp, "forgot-password/request-otp")
            .AllowAnonymous()
            .WithSummary("Yeu cau OTP quen mat khau")
            .WithDescription(
                "Quyen truy cap: Anonymous (khong can token).\n" +
                "API Note:\n" +
                "- Body: email.\n" +
                "- Neu hop le se tra ve challengeId moi cho flow reset password.");

        groupBuilder.MapPost(ResetPassword, "forgot-password/reset")
            .AllowAnonymous()
            .WithSummary("Dat lai mat khau bang OTP")
            .WithDescription(
                "Quyen truy cap: Anonymous (khong can token).\n" +
                "API Note:\n" +
                "- Body: challengeId, code, newPassword.\n" +
                "- Dung challengeId tu /api/auth/forgot-password/request-otp hoac /api/auth/resend-otp.");

        groupBuilder.MapPost(ChangePassword, "change-password")
            .RequireAuthorization()
            .WithSummary("Doi mat khau khi da dang nhap")
            .WithDescription(
                "Quyen truy cap: Customer, Staff, Manager, Admin System da dang nhap va con hoat dong.\n" +
                "API Note:\n" +
                "- Header: Authorization: Bearer <accessToken>.\n" +
                "- Body: currentPassword, newPassword.");

        groupBuilder.MapPost(RefreshToken, "refresh")
            .AllowAnonymous()
            .WithSummary("Lam moi token")
            .WithDescription(
                "Quyen truy cap: Anonymous (khong can access token).\n" +
                "API Note:\n" +
                "- Body: refreshToken.\n" +
                "- Tra ve accessToken moi va refreshToken moi.");

        groupBuilder.MapGet("me", Me)
            .WithName(nameof(Me))
            .RequireAuthorization()
            .WithSummary("Lay profile user hien tai")
            .WithDescription(
                "Quyen truy cap: Customer, Staff, Manager, Admin System da dang nhap.\n" +
                "API Note:\n" +
                "- Header: Authorization: Bearer <accessToken>.\n" +
                "- Dung de kiem tra token va doc thong tin user dang dang nhap.");

        groupBuilder.MapPost(Logout, "logout")
            .RequireAuthorization()
            .WithSummary("Dang xuat")
            .WithDescription(
                "Quyen truy cap: Customer, Staff, Manager, Admin System da dang nhap.\n" +
                "API Note:\n" +
                "- Header: Authorization: Bearer <accessToken>.\n" +
                "- Endpoint nay revoke refresh token con hieu luc cua user.");
    }

    private static async Task<IResult> Register(ISender sender, RegisterCommand command, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(command, cancellationToken));

    private static async Task<IResult> VerifyRegisterOtp(ISender sender, VerifyRegisterOtpCommand command, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(command, cancellationToken));

    private static async Task<IResult> ResendOtp(ISender sender, ResendOtpCommand command, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(command, cancellationToken));

    private static async Task<IResult> Login(ISender sender, LoginCommand command, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(command, cancellationToken));

    private static async Task<IResult> GoogleLogin(ISender sender, GoogleLoginCommand command, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(command, cancellationToken));

    private static async Task<IResult> ForgotPasswordRequestOtp(ISender sender, ForgotPasswordRequestOtpCommand command, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(command, cancellationToken));

    private static async Task<IResult> ResetPassword(ISender sender, ResetPasswordCommand command, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(command, cancellationToken));

    private static async Task<IResult> ChangePassword(ISender sender, ChangePasswordCommand command, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(command, cancellationToken));

    private static async Task<IResult> RefreshToken(ISender sender, RefreshTokenCommand command, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(command, cancellationToken));

    private static async Task<IResult> Me(ISender sender, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetCurrentUserProfileQuery(), cancellationToken));

    private static async Task<IResult> Logout(ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new LogoutCommand(), cancellationToken);
        return Results.NoContent();
    }
}
