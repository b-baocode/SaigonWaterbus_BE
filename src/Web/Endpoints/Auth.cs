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
            .AllowAnonymous();

        groupBuilder.MapPost(VerifyRegisterOtp, "register/verify-otp")
            .AllowAnonymous();

        groupBuilder.MapPost(ResendOtp, "resend-otp")
            .AllowAnonymous();

        groupBuilder.MapPost(Login, "login")
            .AllowAnonymous();

        groupBuilder.MapPost(GoogleLogin, "login/google")
            .AllowAnonymous();

        groupBuilder.MapPost(ForgotPasswordRequestOtp, "forgot-password/request-otp")
            .AllowAnonymous();

        groupBuilder.MapPost(ResetPassword, "forgot-password/reset")
            .AllowAnonymous();

        groupBuilder.MapPost(ChangePassword, "change-password")
            .RequireAuthorization();

        groupBuilder.MapPost(RefreshToken, "refresh")
            .AllowAnonymous()
            .ExcludeFromDescription();

        groupBuilder.MapGet("me", Me)
            .WithName(nameof(Me))
            .RequireAuthorization();

        groupBuilder.MapPost(Logout, "logout")
            .RequireAuthorization();
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
