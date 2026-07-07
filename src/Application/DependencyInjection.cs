using System.Reflection;
using Microsoft.Extensions.Hosting;
using SaigonWaterbus.Application.Auth;
using SaigonWaterbus.Application.Auth.Login;
using SaigonWaterbus.Application.Auth.Otp;
using SaigonWaterbus.Application.Auth.Password;
using SaigonWaterbus.Application.Auth.Profile;
using SaigonWaterbus.Application.Auth.Register;
using SaigonWaterbus.Application.Auth.Token;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Behaviours;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Common.Validation;
using SaigonWaterbus.Application.Operations;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Application.Boats;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
        });

        builder.Services.AddAutoMapper(cfg =>
            cfg.AddMaps(Assembly.GetExecutingAssembly()));

        builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        builder.Services.AddScoped<IRequestValidator, RequestValidator>();
        builder.Services.AddScoped<IOperationScheduleSynchronizer, OperationScheduleSynchronizer>();
        builder.Services.AddScoped<ICharterBookingExpirationProcessor, CharterBookingExpirationProcessor>();

        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<RegisterRequestUseCase>();
        builder.Services.AddScoped<VerifyRegisterOtpRequestUseCase>();
        builder.Services.AddScoped<ResendOtpRequestUseCase>();
        builder.Services.AddScoped<LoginRequestUseCase>();
        builder.Services.AddScoped<GoogleLoginRequestUseCase>();
        builder.Services.AddScoped<RefreshTokenRequestUseCase>();
        builder.Services.AddScoped<LogoutRequestUseCase>();
        builder.Services.AddScoped<GetCurrentUserProfileRequestUseCase>();
        builder.Services.AddScoped<UpdateCurrentUserProfileRequestUseCase>();
        builder.Services.AddScoped<DeleteCurrentUserAccountRequestUseCase>();
        builder.Services.AddScoped<VerifyEmailChangeOtpRequestUseCase>();
        builder.Services.AddScoped<VerifyPhoneChangeOtpRequestUseCase>();
        builder.Services.AddScoped<ForgotPasswordOtpRequestUseCase>();
        builder.Services.AddScoped<ResetPasswordRequestUseCase>();
        builder.Services.AddScoped<ChangePasswordRequestUseCase>();

        builder.Services.AddScoped<IUserManagementService, UserManagementService>();
        builder.Services.AddScoped<GetUsersRequestUseCase>();
        builder.Services.AddScoped<GetUserByIdRequestUseCase>();
        builder.Services.AddScoped<GetManageableRolesRequestUseCase>();
        builder.Services.AddScoped<CreateUserRequestUseCase>();
        builder.Services.AddScoped<UpdateUserRequestUseCase>();
        builder.Services.AddScoped<UpdateUserStatusRequestUseCase>();
        builder.Services.AddScoped<ResetManagedUserPasswordRequestUseCase>();
        builder.Services.AddScoped<GetUserStationAssignmentsRequestUseCase>();
        builder.Services.AddScoped<AssignUserStationsRequestUseCase>();
        builder.Services.AddScoped<DeleteUserRequestUseCase>();

        builder.Services.AddScoped<IBoatManagementService, BoatManagementService>();
        builder.Services.AddScoped<GetBoatsRequestUseCase>();
        builder.Services.AddScoped<GetBoatByIdRequestUseCase>();
        builder.Services.AddScoped<CreateBoatRequestUseCase>();
        builder.Services.AddScoped<UpdateBoatRequestUseCase>();
        builder.Services.AddScoped<UpdateBoatStatusRequestUseCase>();
        builder.Services.AddScoped<DeleteBoatRequestUseCase>();

        builder.Services.AddScoped<ISeatManagementService, SeatManagementService>();
        builder.Services.AddScoped<GetSeatsRequestUseCase>();
        builder.Services.AddScoped<GenerateSeatMatrixRequestUseCase>();
        builder.Services.AddScoped<GenerateSeatsRequestUseCase>();
        builder.Services.AddScoped<UpdateSeatRequestUseCase>();
        builder.Services.AddScoped<UpdateSeatStatusRequestUseCase>();
        builder.Services.AddScoped<DeleteSeatRequestUseCase>();
        builder.Services.AddScoped<DeleteAllSeatsRequestUseCase>();

    }
}
