using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Auth;
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
          "gender": "Male",
          "nationality": "Vietnamese",
          "phone": "+84901234567",
          "email": "vana@gmail.com",
          "otpChannel": "phone",
          "password": "P@ssword123"
        }
        """;

    private const string VerifyRegisterOtpExample =
        """
        {
          "challengeId": "019ecf65-3496-7c9c-8792-4e9fbc31cb5b",
          "code": "123456"
        }
        """;

    private const string ResendOtpExample =
        """
        {
          "challengeId": "019ecf65-3496-7c9c-8792-4e9fbc31cb5b"
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
          "gender": "Male",
          "nationality": "Vietnamese",
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

    private const string VerifyPhoneChangeOtpExample =
        """
        {
          "challengeId": 32,
          "code": "123456"
        }
        """;

    public static string RoutePrefix => "/api/auth";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(Register, "register")
            .WithSummary("Đăng ký customer")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                RegisterExample,
                "Tạo user ở trạng thái PendingVerification.",
                "Cần có ít nhất email hoặc số điện thoại.",
                "Nếu chỉ có email thì OTP mặc định gửi về email.",
                "Nếu chỉ có số điện thoại thì OTP mặc định gửi về SMS.",
                "Nếu có cả email và số điện thoại thì frontend phải cho user chọn kênh và gửi otpChannel=email hoặc otpChannel=phone.",
                "gender và nationality không bắt buộc; nếu không gửi thì backend lưu null.",
                "Do hệ thống xác thực số điện thoại khi đăng ký, otpChannel=phone là lựa chọn an toàn khi có số điện thoại.",
                "Trả về challengeId để gọi /api/auth/verify-register-otp."));

        groupBuilder.MapPost(VerifyRegisterOtp, "verify-register-otp")
            .WithSummary("Xác nhận OTP đăng ký")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                VerifyRegisterOtpExample,
                "Dùng challengeId trả về từ /api/auth/register.",
                "Nếu đã gửi lại OTP, ưu tiên challengeId mới nhất trả về từ /api/auth/resend-otp.",
                "Thành công sẽ kích hoạt tài khoản."));

        groupBuilder.MapPost(ResendOtp, "resend-otp")
            .WithSummary("Gửi lại OTP")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ResendOtpExample,
                "Dùng cho đăng ký, quên mật khẩu hoặc xác thực email mới.",
                "Nếu challenge là xác thực email mới thì cần Authorization Bearer token của user hiện tại.",
                "Chỉ gửi lại khi đã qua thời gian chờ resend.",
                "Response có challengeId mới nhất để verify OTP."));

        groupBuilder.MapPost(Login, "login")
            .WithSummary("Đăng nhập bằng email hoặc số điện thoại")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                LoginExample,
                "Đăng nhập bằng email hoặc số điện thoại và mật khẩu.",
                "Frontend gửi định danh người dùng vào emailOrPhone.",
                "Số điện thoại chỉ hỗ trợ số Việt Nam, ví dụ 0901234567 hoặc +84901234567.",
                "Tài khoản phải đã xác minh OTP.",
                "Trả về thông tin user, access token và refresh token."));

        groupBuilder.MapPost(GoogleLogin, "google/login")
            .WithSummary("Đăng nhập bằng Google")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                GoogleLoginExample,
                "Frontend gửi idToken sau khi đăng nhập Google.",
                "User cũ sẽ được cấp token nếu tài khoản không bị khóa.",
                "User mới sẽ được tạo tài khoản Customer Active bằng email Google và cấp token.",
                "Không bắt buộc số điện thoại trong luồng Google login."));

        groupBuilder.MapPost(RefreshToken, "refresh-token")
            .WithSummary("Làm mới access token")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                RefreshTokenExample,
                "Dùng refreshToken trả về từ /api/auth/login hoặc /api/auth/google/login.",
                "Refresh token cũ sẽ bị revoke sau khi đổi token mới."));

        groupBuilder.MapPost(ForgotPassword, "forgot-password")
            .WithSummary("Yêu cầu OTP quên mật khẩu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ForgotPasswordExample,
                "Nhập email hoặc số điện thoại vào emailOrPhone. Nếu là email thì OTP gửi về email, nếu là số điện thoại thì OTP gửi về SMS.",
                "Trả về challengeId để gọi /api/auth/reset-password.",
                "Số điện thoại chỉ hỗ trợ số Việt Nam, ví dụ 0901234567 hoặc +84901234567."));

        groupBuilder.MapPost(ResetPassword, "reset-password")
            .WithSummary("Đặt lại mật khẩu bằng OTP")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                ResetPasswordExample,
                "Dùng challengeId trả về từ /api/auth/forgot-password.",
                "Nếu đã gửi lại OTP, ưu tiên challengeId mới nhất trả về từ /api/auth/resend-otp.",
                "Thành công sẽ revoke refresh token đang còn hiệu lực."));

        groupBuilder.MapPost(Logout, "logout")
            .RequireAuthorization()
            .WithSummary("Đăng xuất")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Header cần có Authorization: Bearer <accessToken>.",
                "Tất cả refresh token còn hiệu lực của user hiện tại sẽ bị revoke."));

        groupBuilder.MapPost(ChangePassword, "change-password")
            .RequireAuthorization()
            .WithSummary("Đổi mật khẩu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                ChangePasswordExample,
                "Header cần có Authorization: Bearer <accessToken>.",
                "NewPassword phải khác CurrentPassword."));

        groupBuilder.MapGet(Me, "me")
            .RequireAuthorization()
            .WithSummary("Lấy profile hiện tại")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Header cần có Authorization: Bearer <accessToken>.",
                "Trả về thông tin profile và role của user đang đăng nhập."));

        groupBuilder.MapPut(UpdateMe, "me")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<UpdateCurrentUserProfileJsonRequest>("application/json", "multipart/form-data")
            .WithSummary("Cập nhật profile hiện tại")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateMeExample,
                "User được cập nhật fullName, dateOfBirth, gender, nationality, phoneNumber, email và ảnh đại diện.",
                "Chỉ field nào gửi lên mới được cập nhật; field không gửi sẽ giữ dữ liệu cũ.",
                "Có thể gửi application/json nếu không đổi ảnh.",
                "Nếu đổi ảnh, gửi multipart/form-data với các field fullName, dateOfBirth, gender, nationality, phoneNumber, email và file.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "User chưa có số điện thoại có thể thêm phoneNumber một lần; backend chỉ gửi OTP và chưa lưu số cho tới khi gọi verify-phone-change-otp thành công.",
                "User đã có số điện thoại không được tự đổi phoneNumber; Admin hoặc Manager đổi số điện thoại customer qua API quản lý user.",
                "Nếu email thay đổi, backend gửi OTP tới email mới và chưa đổi email cho tới khi verify OTP."));

        groupBuilder.MapDelete(DeleteMe, "me")
            .RequireAuthorization()
            .WithSummary("Tự xóa tài khoản customer")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer",
                null,
                "Header cần có Authorization: Bearer <accessToken>.",
                "Chỉ tài khoản Customer được tự xóa tài khoản của chính mình.",
                "Manager, Staff và Admin không được dùng endpoint này."));

        groupBuilder.MapPost(VerifyEmailChangeOtp, "verify-email-change-otp")
            .RequireAuthorization()
            .WithSummary("Xác thực email mới")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                VerifyEmailChangeOtpExample,
                "Dùng challengeId trả về từ PUT /api/auth/me khi thay đổi email.",
                "Thành công sẽ cập nhật email mới."));

        groupBuilder.MapPost(VerifyPhoneChangeOtp, "verify-phone-change-otp")
            .RequireAuthorization()
            .WithSummary("Xác thực số điện thoại mới")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                VerifyPhoneChangeOtpExample,
                "Dùng challengeId trả về từ PUT /api/auth/me khi user thêm số điện thoại lần đầu.",
                "Thành công sẽ cập nhật phoneNumber và PhoneVerifiedAt.",
                "Trước khi verify thành công, số điện thoại mới chưa dùng được để login."));
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

    public static async Task<IResult> VerifyPhoneChangeOtp(
        IAuthService authService,
        VerifyPhoneChangeOtpRequest command,
        CancellationToken cancellationToken)
    {
        var result = await authService.VerifyPhoneChangeOtpAsync(command, cancellationToken);
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
            profileRequest?.Gender,
            profileRequest?.Nationality,
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
            GetOptionalFormValue(form, "gender"),
            GetOptionalFormValue(form, "nationality"),
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
        string? Gender = null,
        string? Nationality = null,
        string? PhoneNumber = null,
        string? Email = null);

    private sealed record UpdateCurrentUserProfileFormRequest(
        string? FullName = null,
        DateOnly? DateOfBirth = null,
        string? Gender = null,
        string? Nationality = null,
        string? PhoneNumber = null,
        string? Email = null,
        IFormFile? File = null);
}
