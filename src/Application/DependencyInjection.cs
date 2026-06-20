using System.Reflection;
using Microsoft.Extensions.Hosting;
using SaigonWaterbus.Application.Auth;
using SaigonWaterbus.Application.Auth.Login;
using SaigonWaterbus.Application.Auth.Otp;
using SaigonWaterbus.Application.Auth.Password;
using SaigonWaterbus.Application.Auth.Profile;
using SaigonWaterbus.Application.Auth.Register;
using SaigonWaterbus.Application.Auth.Token;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Common.Validation;
using SaigonWaterbus.Application.Operations;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Application.Vessels;
using SaigonWaterbus.Application.WaterbusServices;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

        builder.Services.AddAutoMapper(cfg =>
            cfg.AddMaps(Assembly.GetExecutingAssembly()));

        builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        builder.Services.AddScoped<IRequestValidator, RequestValidator>();
        builder.Services.AddScoped<IOperationScheduleSynchronizer, OperationScheduleSynchronizer>();

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
        builder.Services.AddScoped<GetUserStationAssignmentsRequestUseCase>();
        builder.Services.AddScoped<AssignUserStationsRequestUseCase>();
        builder.Services.AddScoped<DeleteUserRequestUseCase>();

        builder.Services.AddScoped<IVesselManagementService, VesselManagementService>();
        builder.Services.AddScoped<GetVesselsRequestUseCase>();
        builder.Services.AddScoped<GetVesselByIdRequestUseCase>();
        builder.Services.AddScoped<CreateVesselRequestUseCase>();
        builder.Services.AddScoped<UpdateVesselRequestUseCase>();
        builder.Services.AddScoped<UpdateVesselStatusRequestUseCase>();
        builder.Services.AddScoped<UpdateVesselRentalPriceRequestUseCase>();
        builder.Services.AddScoped<DeleteVesselRequestUseCase>();

        builder.Services.AddScoped<ISeatManagementService, SeatManagementService>();
        builder.Services.AddScoped<GetSeatsRequestUseCase>();
        builder.Services.AddScoped<GenerateSeatMatrixRequestUseCase>();
        builder.Services.AddScoped<GenerateSeatsRequestUseCase>();
        builder.Services.AddScoped<UpdateSeatRequestUseCase>();
        builder.Services.AddScoped<UpdateSeatStatusRequestUseCase>();
        builder.Services.AddScoped<DeleteSeatRequestUseCase>();
        builder.Services.AddScoped<DeleteAllSeatsRequestUseCase>();

        builder.Services.AddScoped<IWaterbusServiceManagementService, WaterbusServiceManagementService>();
        builder.Services.AddScoped<GetWaterbusServicesRequestUseCase>();
        builder.Services.AddScoped<GetWaterbusServiceByIdRequestUseCase>();
        builder.Services.AddScoped<GetWaterbusServiceSeatTypesRequestUseCase>();
        builder.Services.AddScoped<CreateWaterbusServiceRequestUseCase>();
        builder.Services.AddScoped<UpdateWaterbusServiceRequestUseCase>();
        builder.Services.AddScoped<UpdateWaterbusServiceStatusRequestUseCase>();
        builder.Services.AddScoped<DeleteWaterbusServiceRequestUseCase>();
    }
}
