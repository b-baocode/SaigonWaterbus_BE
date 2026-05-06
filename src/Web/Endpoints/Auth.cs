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

namespace SaigonWaterbus.Web.Endpoints;

public class Auth : IEndpointGroup
{
    private const string RegisterExample =
        """
        {
          "fullName": "Nguyen Van A",
          "dateOfBirth": "02/09/2003",
          "phone": "0901234567",
          "password": "P@ssword123",
          "email": "vana@gmail.com",
          "otpChannel": "phone"
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
          "phone": "0901234567",
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

    private const string UpdateProfileExample =
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

    public static string OpenApiTag => string.Empty;

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        var registration = groupBuilder.MapGroup("").WithTags("01 Auth - Registration");
        var session = groupBuilder.MapGroup("").WithTags("02 Auth - Login Session");
        var password = groupBuilder.MapGroup("").WithTags("03 Auth - Password");
        var profile = groupBuilder.MapGroup("").WithTags("04 Auth - Profile");

        registration.MapPost(Register, "register")
            .WithSummary("Dang ky customer")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                RegisterExample,
                "Dang ky tai khoan CUSTOMER.",
                "Tra challengeId de xac minh OTP."));

        registration.MapPost(VerifyRegisterOtp, "verify-register-otp")
            .WithSummary("Xac nhan OTP dang ky")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                VerifyRegisterOtpExample,
                "Xac minh OTP dang ky.",
                "Thanh cong se kich hoat tai khoan."));

        registration.MapPost(ResendOtp, "resend-otp")
            .WithSummary("Gui lai OTP")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ResendOtpExample,
                "Gui lai OTP cho challenge con hieu luc.",
                "Dung challengeId moi nhat de verify."));

        session.MapPost(Login, "login")
            .WithSummary("Dang nhap bang so dien thoai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                LoginExample,
                "Dang nhap bang phone va password.",
                "Tra user, access token va refresh token."));

        session.MapPost(GoogleLogin, "google-login")
            .WithSummary("Dang nhap bang Google")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                GoogleLoginExample,
                "Validate Google idToken va dang nhap ngay.",
                "Lan dau tao/link Google se gui email thong bao."));

        session.MapPost(RefreshToken, "refresh-token")
            .WithSummary("Lam moi access token")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                RefreshTokenExample,
                "Doi refresh token lay token moi.",
                "Refresh token cu se bi revoke."));

        password.MapPost(ForgotPassword, "forgot-password")
            .WithSummary("Yeu cau OTP quen mat khau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ForgotPasswordExample,
                "Gui OTP quen mat khau qua email hoac phone.",
                "Tra challengeId de reset password."));

        password.MapPost(ResetPassword, "reset-password")
            .WithSummary("Dat lai mat khau bang OTP")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ResetPasswordExample,
                "Dat lai mat khau bang OTP.",
                "Thanh cong se revoke refresh token."));

        session.MapPost(Logout, "logout")
            .RequireAuthorization()
            .WithSummary("Dang xuat")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Dang xuat user hien tai.",
                "Revoke refresh token con hieu luc."));

        password.MapPost(ChangePassword, "change-password")
            .RequireAuthorization()
            .WithSummary("Doi mat khau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                ChangePasswordExample,
                "Doi mat khau user dang login.",
                "Mat khau moi phai khac mat khau cu."));

        profile.MapGet(GetProfile, "profile")
            .RequireAuthorization()
            .WithSummary("Lay profile hien tai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Lay profile user dang login."));

        profile.MapPut(UpdateProfile, "profile/update")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<UpdateCurrentUserProfileJsonRequest>("application/json", "multipart/form-data")
            .WithSummary("Cap nhat profile hien tai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateProfileExample,
                "Cap nhat cac field duoc gui len.",
                "Doi email can verify OTP; doi avatar dung multipart/form-data."));

        profile.MapDelete(DeleteProfile, "profile/delete")
            .RequireAuthorization()
            .WithSummary("Tu xoa tai khoan customer")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer",
                null,
                "Customer tu xoa tai khoan cua minh.",
                "Internal account khong dung endpoint nay."));

        profile.MapPost(VerifyEmailChangeOtp, "profile/verify-email-change-otp")
            .RequireAuthorization()
            .WithSummary("Xac thuc email moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                VerifyEmailChangeOtpExample,
                "Xac minh OTP doi email.",
                "Thanh cong se cap nhat email moi."));

        profile.MapPut(UpdateProfile, "profile")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<UpdateCurrentUserProfileJsonRequest>("application/json", "multipart/form-data")
            .WithName("UpdateProfileLegacy")
            .ExcludeFromDescription();

        profile.MapDelete(DeleteProfile, "profile")
            .RequireAuthorization()
            .WithName("DeleteProfileLegacy")
            .ExcludeFromDescription();

        profile.MapPost(VerifyEmailChangeOtp, "verify-email-change-otp")
            .RequireAuthorization()
            .WithName("VerifyEmailChangeOtpLegacy")
            .ExcludeFromDescription();
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

    public static async Task<IResult> GetProfile(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCurrentUserProfileQuery(), cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> UpdateProfile(
        ISender sender,
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
            var result = await sender.Send(command, cancellationToken);
            return Results.Ok(result);
        }
        finally
        {
            command.AvatarContent?.Dispose();
        }
    }

    public static async Task<IResult> DeleteProfile(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteCurrentUserAccountCommand(), cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> VerifyEmailChangeOtp(
        ISender sender,
        VerifyEmailChangeOtpCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
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

    private static async Task<UpdateCurrentUserProfileCommand> CreateUpdateProfileCommandFromJsonAsync(
        HttpRequest request,
        JsonSerializerOptions jsonSerializerOptions,
        CancellationToken cancellationToken)
    {
        var profileRequest = await request.ReadFromJsonAsync<UpdateCurrentUserProfileJsonRequest>(
            jsonSerializerOptions,
            cancellationToken: cancellationToken);

        return new UpdateCurrentUserProfileCommand(
            profileRequest?.FullName,
            profileRequest?.DateOfBirth,
            profileRequest?.PhoneNumber,
            profileRequest?.Email);
    }

    private static async Task<UpdateCurrentUserProfileCommand> CreateUpdateProfileCommandFromFormAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"]
            ?? form.Files["File"]
            ?? form.Files.FirstOrDefault();
        var avatarStream = file?.OpenReadStream();

        return new UpdateCurrentUserProfileCommand(
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
                ["dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        throw new BadHttpRequestException("dateOfBirth phải dùng định dạng dd/MM/yyyy, dd-MM-yyyy hoặc yyyy-MM-dd.");
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
