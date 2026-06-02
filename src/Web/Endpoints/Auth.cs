using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Auth.Login;
using SaigonWaterbus.Application.Auth.Otp;
using SaigonWaterbus.Application.Auth.Password;
using SaigonWaterbus.Application.Auth.Profile;
using SaigonWaterbus.Application.Auth.Register;
using SaigonWaterbus.Application.Auth.Token;
using SaigonWaterbus.Application.Auth;

namespace SaigonWaterbus.Web.Endpoints;

public class Auth : IEndpointGroup
{
    private const string RegisterExample =
        """
        {
          "fullName": "Nguyen Van A",
          "dateOfBirth": "02/09/2003",
          "password": "P@ssword123",
          "email": "vana@gmail.com"
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
          "emailOrPhone": "vana@gmail.com",
          "password": "P@ssword123"
        }
        """;

    private const string GoogleLoginExample =
        """
        {
          "idToken": "google-id-token-from-frontend"
        }
        """;

    private const string GoogleSendPhoneOtpExample =
        """
        {
          "tempToken": "google-temp-token",
          "phone": "0901234567"
        }
        """;

    private const string GoogleVerifyPhoneExample =
        """
        {
          "tempToken": "google-temp-token",
          "otp": "123456"
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
          "emailOrPhone": "customer@example.com"
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

    private const string UpdateMeExample =
        """
        {
          "fullName": "Nguyen Van A Updated",
          "dateOfBirth": "02/09/2003",
          "phoneNumber": "+84901234567",
          "email": "vana@gmail.com"
        }
        """;

    private const string VerifyEmailChangeOtpExample =
        """
        {
          "challengeId": 31,
          "code": "123456"
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
                "Can co it nhat email hoac so dien thoai.",
                "Neu chi co email thi OTP mac dinh gui ve email.",
                "Neu chi co so dien thoai thi OTP mac dinh gui ve SMS.",
                "Neu co ca email va so dien thoai thi OTP gui ve so dien thoai.",
                "Tra ve challengeId de goi /api/auth/verify-register-otp."));

        groupBuilder.MapPost(VerifyRegisterOtp, "verify-register-otp")
            .WithSummary("Xac nhan OTP dang ky")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                VerifyRegisterOtpExample,
                "Dung challengeId tra ve tu /api/auth/register.",
                "Neu da gui lai OTP, uu tien challengeId moi nhat tra ve tu /api/auth/resend-otp.",
                "Thanh cong se kich hoat tai khoan."));

        groupBuilder.MapPost(ResendOtp, "resend-otp")
            .WithSummary("Gui lai OTP")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ResendOtpExample,
                "Dung cho dang ky, quen mat khau hoac xac thuc email moi.",
                "Neu challenge la xac thuc email moi thi can Authorization Bearer token cua user hien tai.",
                "Chi gui lai khi da qua thoi gian cho resend.",
                "Response co challengeId moi nhat de verify OTP."));

        groupBuilder.MapPost(Login, "login")
            .WithSummary("Dang nhap bang email hoac so dien thoai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                LoginExample,
                "Dang nhap bang email hoac so dien thoai va mat khau.",
                "Frontend gui dinh danh nguoi dung vao emailOrPhone.",
                "So dien thoai chi ho tro so Viet Nam, vi du 0901234567 hoac +84901234567.",
                "Tai khoan phai da xac minh OTP.",
                "Tra ve thong tin user, access token va refresh token."));

        groupBuilder.MapPost(GoogleLogin, "google-login")
            .WithSummary("Dang nhap bang Google")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                GoogleLoginExample,
                "idToken lay tu frontend sau khi dang nhap Google.",
                "User cu da active va co PhoneVerifiedAt se duoc cap token.",
                "User moi hoac user Google cu chua co PhoneVerifiedAt se nhan status NEED_PHONE va tempToken.",
                "Khong tao user moi va khong cap JWT khi chua xac minh so dien thoai."));

        groupBuilder.MapPost(SendGooglePhoneOtp, "google/send-phone-otp")
            .WithSummary("Gui OTP so dien thoai cho Google Login")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                GoogleSendPhoneOtpExample,
                "Dung tempToken tra ve tu /api/auth/google-login.",
                "Backend check phone chua bi user khac dung, sau do gui OTP.",
                "Khong tao user va khong cap JWT o buoc nay."));

        groupBuilder.MapPost(VerifyGooglePhone, "google/verify-phone")
            .WithSummary("Xac minh OTP Google Login va tao user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                GoogleVerifyPhoneExample,
                "Dung tempToken da nhan o buoc gui OTP.",
                "Backend su dung so dien thoai da luu trong temp session, khong can gui lai phone.",
                "OTP dung moi tao hoac hoan tat user that trong database.",
                "Thanh cong cap access token va refresh token."));

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
                "Nhap email hoac so dien thoai vao emailOrPhone. Neu la email thi OTP gui ve email, neu la so dien thoai thi OTP gui ve SMS.",
                "Tra ve challengeId de goi /api/auth/reset-password.",
                "So dien thoai chi ho tro so Viet Nam, vi du 0901234567 hoac +84901234567."));

        groupBuilder.MapPost(ResetPassword, "reset-password")
            .WithSummary("Dat lai mat khau bang OTP")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ResetPasswordExample,
                "Dung challengeId tra ve tu /api/auth/forgot-password.",
                "Neu da gui lai OTP, uu tien challengeId moi nhat tra ve tu /api/auth/resend-otp.",
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

        groupBuilder.MapPut(UpdateMe, "me")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<UpdateCurrentUserProfileJsonRequest>("application/json", "multipart/form-data")
            .WithSummary("Cap nhat profile hien tai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateMeExample,
                "User duoc cap nhat fullName, dateOfBirth, phoneNumber, email va anh dai dien.",
                "Chi field nao gui len moi duoc cap nhat; field khong gui se giu du lieu cu.",
                "Co the gui application/json neu khong doi anh.",
                "Neu doi anh, gui multipart/form-data voi cac field fullName, dateOfBirth, phoneNumber, email va file.",
                "Anh chi ho tro JPEG, PNG hoac WebP, toi da 5 MB.",
                "Customer khong duoc tu thay doi phoneNumber; Admin hoac Manager doi so dien thoai customer qua API quan ly user.",
                "Neu email thay doi, backend gui OTP toi email moi va chua doi email cho toi khi verify OTP."));

        groupBuilder.MapDelete(DeleteMe, "me")
            .RequireAuthorization()
            .WithSummary("Tu xoa tai khoan customer")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer",
                null,
                "Header can co Authorization: Bearer <accessToken>.",
                "Chi tai khoan Customer duoc tu xoa tai khoan cua chinh minh.",
                "Manager, Staff va Admin System khong duoc dung endpoint nay."));

        groupBuilder.MapPost(VerifyEmailChangeOtp, "verify-email-change-otp")
            .RequireAuthorization()
            .WithSummary("Xac thuc email moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                VerifyEmailChangeOtpExample,
                "Dung challengeId tra ve tu PUT /api/auth/me khi thay doi email.",
                "Thanh cong se cap nhat email moi."));
    }

    public static async Task<IResult> Register(
        IAuthService authService,
        RegisterRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> VerifyRegisterOtp(
        IAuthService authService,
        VerifyRegisterOtpRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.VerifyRegisterOtpAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> ResendOtp(
        IAuthService authService,
        ResendOtpRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.ResendOtpAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> Login(
        IAuthService authService,
        LoginRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> GoogleLogin(
        IAuthService authService,
        GoogleLoginRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.GoogleLoginAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> SendGooglePhoneOtp(
        IAuthService authService,
        SendGooglePhoneOtpRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.SendGooglePhoneOtpAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> VerifyGooglePhone(
        IAuthService authService,
        VerifyGooglePhoneRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.VerifyGooglePhoneAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> RefreshToken(
        IAuthService authService,
        RefreshTokenRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.RefreshTokenAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> Logout(
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(cancellationToken);
        return Results.NoContent();
    }

    public static async Task<IResult> Me(
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.GetCurrentUserProfileAsync(cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> UpdateMe(
        IAuthService authService,
        HttpRequest request,
        IOptions<JsonOptions> jsonOptions,
        CancellationToken cancellationToken)
    {
        var command = request.HasFormContentType
            ? await CreateUpdateProfileCommandFromFormAsync(request, cancellationToken)
            : await CreateUpdateProfileCommandFromJsonAsync(
                request,
                jsonOptions.Value.SerializerOptions,
                cancellationToken);

        try
        {
            var result = await authService.UpdateCurrentUserProfileAsync(command, cancellationToken);
            return Results.Ok(result);
        }
        finally
        {
            command.AvatarContent?.Dispose();
        }
    }

    public static async Task<IResult> DeleteMe(
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.DeleteCurrentUserAccountAsync(cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> VerifyEmailChangeOtp(
        IAuthService authService,
        VerifyEmailChangeOtpRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.VerifyEmailChangeOtpAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> ForgotPassword(
        IAuthService authService,
        ForgotPasswordOtpRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.ForgotPasswordAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> ResetPassword(
        IAuthService authService,
        ResetPasswordRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.ResetPasswordAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> ChangePassword(
        IAuthService authService,
        ChangePasswordRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.ChangePasswordAsync(command, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<UpdateCurrentUserProfileRequest> CreateUpdateProfileCommandFromJsonAsync(
        HttpRequest request,
        JsonSerializerOptions jsonSerializerOptions,
        CancellationToken cancellationToken)
    {
        var profileRequest = await request.ReadFromJsonAsync<UpdateCurrentUserProfileJsonRequest>(
            jsonSerializerOptions,
            cancellationToken: cancellationToken);

        return new UpdateCurrentUserProfileRequest(
            profileRequest?.FullName,
            profileRequest?.DateOfBirth,
            profileRequest?.PhoneNumber,
            profileRequest?.Email);
    }

    private static async Task<UpdateCurrentUserProfileRequest> CreateUpdateProfileCommandFromFormAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"]
            ?? form.Files["File"]
            ?? form.Files.FirstOrDefault();
        var avatarStream = file?.OpenReadStream();

        return new UpdateCurrentUserProfileRequest(
            GetOptionalFormValue(form, "fullName"),
            ParseOptionalDateOnly(GetOptionalFormValue(form, "dateOfBirth")),
            GetOptionalFormValue(form, "phoneNumber"),
            GetOptionalFormValue(form, "email"),
            file?.FileName,
            file?.ContentType,
            file?.Length,
            avatarStream);
    }

    private static string? GetOptionalFormValue(IFormCollection form, string name)
    {
        var value = form[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static DateOnly? ParseOptionalDateOnly(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                value,
                ["dd/MM/yyyy", "dd-MM-yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        throw new BadHttpRequestException("dateOfBirth phải dùng định dạng dd/MM/yyyy hoặc dd-MM-yyyy.");
    }

    private sealed record UpdateCurrentUserProfileJsonRequest(
        string? FullName = null,
        DateOnly? DateOfBirth = null,
        string? PhoneNumber = null,
        string? Email = null);

    private sealed record UpdateCurrentUserProfileFormRequest(
        string? FullName = null,
        DateOnly? DateOfBirth = null,
        string? PhoneNumber = null,
        string? Email = null,
        IFormFile? File = null);
}
