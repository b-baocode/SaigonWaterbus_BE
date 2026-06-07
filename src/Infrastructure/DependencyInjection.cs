using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Auth;
using SaigonWaterbus.Infrastructure.Data;
using SaigonWaterbus.Infrastructure.Data.Interceptors;
using SaigonWaterbus.Infrastructure.Media;
using SaigonWaterbus.Infrastructure.Options;
using SaigonWaterbus.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    private const string DatabaseConnectionName = "SaigonWaterbusDb";
    private const string BrevoHttpClientName = "Brevo";
    private const string EsmsHttpClientName = "Esms";

    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName);
        Guard.Against.NullOrWhiteSpace(connectionString, message: $"Connection string '{DatabaseConnectionName}' not found.");

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseNpgsql(connectionString);
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        builder.EnrichNpgsqlDbContext<ApplicationDbContext>();

        builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        builder.Services.AddSingleton<IDatabaseExceptionClassifier, NpgsqlDatabaseExceptionClassifier>();
        builder.Services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        builder.Services.AddScoped<IIdentityNormalizer, IdentityNormalizer>();
        builder.Services.AddScoped<ISecretHasher, Pbkdf2SecretHasher>();
        builder.Services.AddScoped<ITokenService, JwtTokenService>();
        builder.Services.AddScoped<IOtpCodeService, OtpCodeService>();
        builder.Services.AddScoped<IOtpPolicy, OtpPolicyAccessor>();
        builder.Services.AddScoped<IUserCodeGenerator, UserCodeGenerator>();
        builder.Services.AddScoped<IProfileImageStorageService, CloudinaryProfileImageStorageService>();
        builder.Services.AddScoped<IVesselImageStorageService, CloudinaryVesselImageStorageService>();
        builder.Services.AddHttpClient(BrevoHttpClientName);
        builder.Services.AddHttpClient(EsmsHttpClientName);
        builder.Services.AddScoped<EsmsSmsSender>();
        builder.Services.AddScoped<ISmsOtpSender, EsmsOtpSender>();
        builder.Services.AddScoped<ILoginNotificationSender>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var brevoEnabled = configuration.GetValue<bool>($"{BrevoOptions.SectionName}:Enabled");
            if (brevoEnabled)
            {
                return ActivatorUtilities.CreateInstance<BrevoLoginNotificationSender>(provider);
            }

            var gmailEnabled = configuration.GetValue<bool>($"{GmailOptions.SectionName}:Enabled");
            if (gmailEnabled)
            {
                return ActivatorUtilities.CreateInstance<GmailLoginNotificationSender>(provider);
            }

            return ActivatorUtilities.CreateInstance<NoOpLoginNotificationSender>(provider);
        });
        builder.Services.AddScoped<IOtpSender>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var brevoEnabled = configuration.GetValue<bool>($"{BrevoOptions.SectionName}:Enabled");
            if (brevoEnabled)
            {
                return ActivatorUtilities.CreateInstance<BrevoOtpSender>(provider);
            }

            var gmailEnabled = configuration.GetValue<bool>($"{GmailOptions.SectionName}:Enabled");
            if (gmailEnabled)
            {
                return ActivatorUtilities.CreateInstance<GmailOtpSender>(provider);
            }

            return ActivatorUtilities.CreateInstance<NoOpOtpSender>(provider);
        });

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();
        builder.Services.AddHostedService<PendingRegistrationCleanupService>();
        builder.Services.Configure<DatabaseStartupSettings>(options =>
        {
            options.ResetOnStartup = builder.Environment.IsDevelopment() &&
                builder.Configuration.GetValue<bool>("Database:ResetOnStartup");
            options.SeedInternalUsers =
                builder.Configuration.GetValue<bool?>("Database:SeedInternalUsers")
                ?? builder.Environment.IsDevelopment();
        });
        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
        builder.Services.Configure<OtpOptions>(builder.Configuration.GetSection(OtpOptions.SectionName));
        builder.Services.Configure<GmailOptions>(builder.Configuration.GetSection(GmailOptions.SectionName));
        builder.Services.Configure<BrevoOptions>(builder.Configuration.GetSection(BrevoOptions.SectionName));
        builder.Services.Configure<LoginNotificationOptions>(builder.Configuration.GetSection(LoginNotificationOptions.SectionName));
        builder.Services.Configure<EsmsOptions>(builder.Configuration.GetSection(EsmsOptions.SectionName));
        builder.Services.Configure<CloudinaryOptions>(builder.Configuration.GetSection(CloudinaryOptions.SectionName));

        builder.Services.AddSingleton(TimeProvider.System);
    }
}
