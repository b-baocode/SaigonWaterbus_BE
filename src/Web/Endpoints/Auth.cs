using SaigonWaterbus.Application.Auth.Login;
using SaigonWaterbus.Application.Auth.Otp;
using SaigonWaterbus.Application.Auth.Password;
using SaigonWaterbus.Application.Auth.Profile;
using SaigonWaterbus.Application.Auth.Register;
using SaigonWaterbus.Application.Auth.Token;

namespace SaigonWaterbus.Web.Endpoints;

public class Auth : IEndpointGroup
{
    private const string RegisterExample =
        """
        {
          "fullName": "Nguyen Van A",
          "dateOfBirth": "02/09/2003",
          "phone": "0901234567",
          "email": "vana@gmail.com",
          "password": "P@ssword123"
        }
        """;

    private const string VerifyRegisterOtpExample =
        """
        {
          "challengeId": 12,
          "code": "123456"
        }
        """;

    private const string ResendOtpExample =
        """
        {
          "challengeId": 12
        }
        """;

    private const string LoginExample =
        """
        {
          "email": "vana@gmail.com",
          "password": "P@ssword123"
        }
        """;

    private const string GoogleLoginExample =
        """
        {
          "idToken": "google-id-token-from-frontend"
        }
        """;

    private const string RefreshTokenExample =
        """
        {
          "refreshToken": "15.XYZ_REFRESH_SECRET"
        }
        """;

    private const string ForgotPasswordExample =
        """
        {
          "email": "vana@gmail.com"
        }
        """;

    private const string ResetPasswordExample =
        """
        {
          "challengeId": 24,
          "code": "123456",
          "newPassword": "NewP@ssword123"
        }
        """;

    private const string ChangePasswordExample =
        """
        {
          "currentPassword": "P@ssword123",
          "newPassword": "NewP@ssword123"
        }
        """;

    public static string RoutePrefix => "/api/auth";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(Register, "register")
            .WithSummary("Dang ky customer")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                RegisterExample,
                "Tao user o trang thai PendingVerification.",
                "Tra ve challengeId de goi /api/auth/verify-register-otp."));

        groupBuilder.MapPost(VerifyRegisterOtp, "verify-register-otp")
            .WithSummary("Xac nhan OTP dang ky")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                VerifyRegisterOtpExample,
                "Dung challengeId tra ve tu /api/auth/register.",
                "Thanh cong se kich hoat tai khoan."));

        groupBuilder.MapPost(ResendOtp, "resend-otp")
            .WithSummary("Gui lai OTP")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ResendOtpExample,
                "Dung cho dang ky hoac quen mat khau.",
                "Chi gui lai khi da qua thoi gian cho resend."));

        groupBuilder.MapPost(Login, "login")
            .WithSummary("Dang nhap bang email")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                LoginExample,
                "Tai khoan phai da xac minh OTP.",
                "Tra ve thong tin user, access token va refresh token."));

        groupBuilder.MapPost(GoogleLogin, "google-login")
            .WithSummary("Dang nhap bang Google")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                GoogleLoginExample,
                "idToken lay tu frontend sau khi dang nhap Google.",
                "Tra ve thong tin user, access token va refresh token."));

        groupBuilder.MapPost(RefreshToken, "refresh-token")
            .WithSummary("Lam moi access token")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                RefreshTokenExample,
                "Dung refreshToken tra ve tu endpoint login hoac google-login.",
                "Refresh token cu se bi revoke sau khi doi token moi."));

        groupBuilder.MapPost(ForgotPassword, "forgot-password")
            .WithSummary("Yeu cau OTP quen mat khau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ForgotPasswordExample,
                "Tra ve challengeId de goi /api/auth/reset-password.",
                "Chi ap dung cho email da ton tai."));

        groupBuilder.MapPost(ResetPassword, "reset-password")
            .WithSummary("Dat lai mat khau bang OTP")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ResetPasswordExample,
                "Dung challengeId tra ve tu /api/auth/forgot-password.",
                "Thanh cong se revoke refresh token dang con hieu luc."));

        groupBuilder.MapPost(Logout, "logout")
            .RequireAuthorization()
            .WithSummary("Dang xuat")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Header can co Authorization: Bearer <accessToken>.",
                "Tat ca refresh token con hieu luc cua user hien tai se bi revoke."));

        groupBuilder.MapPost(ChangePassword, "change-password")
            .RequireAuthorization()
            .WithSummary("Doi mat khau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                ChangePasswordExample,
                "Header can co Authorization: Bearer <accessToken>.",
                "NewPassword phai khac CurrentPassword."));

        groupBuilder.MapGet(Me, "me")
            .RequireAuthorization()
            .WithSummary("Lay profile hien tai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Header can co Authorization: Bearer <accessToken>.",
                "Tra ve thong tin profile va role cua user dang dang nhap."));
    }

    public static async Task<IResult> Register(
        ISender sender,
        RegisterCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> VerifyRegisterOtp(
        ISender sender,
        VerifyRegisterOtpCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> ResendOtp(
        ISender sender,
        ResendOtpCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> Login(
        ISender sender,
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> GoogleLogin(
        ISender sender,
        GoogleLoginCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> RefreshToken(
        ISender sender,
        RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> Logout(
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new LogoutCommand(), cancellationToken);
        return Results.NoContent();
    }

    public static async Task<IResult> Me(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCurrentUserProfileQuery(), cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> ForgotPassword(
        ISender sender,
        ForgotPasswordRequestOtpCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> ResetPassword(
        ISender sender,
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> ChangePassword(
        ISender sender,
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return Results.Ok(result);
    }
}
