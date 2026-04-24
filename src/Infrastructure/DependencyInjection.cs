using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Auth;
using SaigonWaterbus.Infrastructure.Data;
using SaigonWaterbus.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    private const string DatabaseConnectionName = "SaigonWaterbusDb";

    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName);
        Guard.Against.Null(connectionString, message: $"Connection string '{DatabaseConnectionName}' not found.");

        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
        builder.Services.Configure<OtpOptions>(builder.Configuration.GetSection(OtpOptions.SectionName));
        builder.Services.Configure<GmailOptions>(builder.Configuration.GetSection(GmailOptions.SectionName));

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseNpgsql(connectionString);
        });

        builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        builder.Services.AddScoped<IIdentityNormalizer, IdentityNormalizer>();
        builder.Services.AddScoped<ISecretHasher, Pbkdf2SecretHasher>();
        builder.Services.AddScoped<IOtpCodeService, OtpCodeService>();
        builder.Services.AddScoped<IOtpSender, GmailOtpSender>();
        builder.Services.AddScoped<IUserCodeGenerator, UserCodeGenerator>();
        builder.Services.AddScoped<ITokenService, JwtTokenService>();
        builder.Services.AddSingleton<IOtpPolicy, OtpPolicyAccessor>();

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();

        builder.Services.AddSingleton(TimeProvider.System);
    }
}
