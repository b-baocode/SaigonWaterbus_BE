using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Auth;
using SaigonWaterbus.Infrastructure.Data;
using SaigonWaterbus.Infrastructure.Data.Interceptors;
using SaigonWaterbus.Infrastructure.Media;
using SaigonWaterbus.Infrastructure.Options;
using SaigonWaterbus.Infrastructure.Payments;
using SaigonWaterbus.Infrastructure.Redis;
using SaigonWaterbus.Infrastructure.Security;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    private const string DatabaseConnectionName = "SaigonWaterbusDb";
    private const string DatabaseConnectionOverrideKey = "SAIGONWATERBUS_DB_CONNECTION_STRING";
    private const string BrevoHttpClientName = "Brevo";
    private const string EsmsHttpClientName = "Esms";
    private const string PayOsHttpClientName = "PayOS";

    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration[DatabaseConnectionOverrideKey]
            ?? builder.Configuration.GetConnectionString(DatabaseConnectionName);
        Guard.Against.NullOrWhiteSpace(connectionString, message: $"Connection string '{DatabaseConnectionName}' not found.");

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseNpgsql(connectionString, o => o.UseNetTopologySuite());
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
        builder.Services.AddScoped<IBookingCodeGenerator, BookingCodeGenerator>();
        builder.Services.AddScoped<IFareCalculator, FareCalculator>();
        builder.Services.AddScoped<ICharterBookingPaymentGateway, PayOsCharterBookingPaymentGateway>();
        builder.Services.AddScoped<IProfileImageStorageService, CloudinaryProfileImageStorageService>();
        builder.Services.AddScoped<IBoatImageStorageService, CloudinaryBoatImageStorageService>();
        builder.Services.AddScoped<IStationImageStorageService, CloudinaryStationImageStorageService>();
        builder.Services.AddHttpClient(BrevoHttpClientName);
        builder.Services.AddHttpClient(EsmsHttpClientName);
        builder.Services.AddHttpClient(PayOsHttpClientName);
        AddRedis(builder);
        AddRedisBackedServices(builder);
        builder.Services.AddHostedService<BookingHoldExpiryService>();
        builder.Services.AddScoped<EsmsSmsSender>();
        builder.Services.AddScoped<ISmsOtpSender>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var esmsEnabled = configuration.GetValue<bool>($"{EsmsOptions.SectionName}:Enabled");
            if (esmsEnabled)
            {
                return ActivatorUtilities.CreateInstance<EsmsOtpSender>(provider);
            }

            return ActivatorUtilities.CreateInstance<NoOpSmsOtpSender>(provider);
        });
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
        builder.Services.AddScoped<IPaymentNotificationSender>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var brevoEnabled = configuration.GetValue<bool>($"{BrevoOptions.SectionName}:Enabled");
            if (brevoEnabled)
            {
                return ActivatorUtilities.CreateInstance<BrevoPaymentNotificationSender>(provider);
            }

            var gmailEnabled = configuration.GetValue<bool>($"{GmailOptions.SectionName}:Enabled");
            if (gmailEnabled)
            {
                return ActivatorUtilities.CreateInstance<GmailPaymentNotificationSender>(provider);
            }

            return ActivatorUtilities.CreateInstance<NoOpPaymentNotificationSender>(provider);
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
        builder.Services.Configure<DatabaseStartupSettings>(options =>
        {
            options.ApplyMigrationsOnStartup =
                builder.Configuration.GetValue<bool?>("Database:ApplyMigrationsOnStartup")
                ?? builder.Environment.IsDevelopment();
            options.ResetOnStartup = builder.Environment.IsDevelopment() &&
                builder.Configuration.GetValue<bool>("Database:ResetOnStartup");
            options.SeedSampleData =
                builder.Configuration.GetValue<bool?>("Database:SeedSampleData")
                ?? false;
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
        builder.Services.Configure<PayOsOptions>(builder.Configuration.GetSection(PayOsOptions.SectionName));
        builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection(RedisOptions.SectionName));
        builder.Services.Configure<OperationScheduleSyncOptions>(builder.Configuration.GetSection(OperationScheduleSyncOptions.SectionName));

        builder.Services.AddSingleton(TimeProvider.System);
    }

    private static void AddRedis(IHostApplicationBuilder builder)
    {
        var redisOptions = builder.Configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
        if (!redisOptions.Enabled)
        {
            return;
        }

        Guard.Against.NullOrWhiteSpace(
            redisOptions.ConnectionString,
            message: "Redis connection string not found while Redis is enabled.");

        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var configurationOptions = ConfigurationOptions.Parse(redisOptions.ConnectionString);
            configurationOptions.AbortOnConnectFail = false;
            configurationOptions.ClientName = string.IsNullOrWhiteSpace(configurationOptions.ClientName)
                ? "saigon-waterbus-api"
                : configurationOptions.ClientName;

            return ConnectionMultiplexer.Connect(configurationOptions);
        });
    }

    private static void AddRedisBackedServices(IHostApplicationBuilder builder)
    {
        var redisEnabled = builder.Configuration.GetValue<bool>($"{RedisOptions.SectionName}:Enabled");
        if (redisEnabled)
        {
            builder.Services.AddScoped<IOtpCache, RedisOtpCache>();
            builder.Services.AddScoped<IBoatHoldService, RedisBoatHoldService>();
            builder.Services.AddScoped<IPaymentProcessingLock, RedisPaymentProcessingLock>();
            builder.Services.AddScoped<ISeatHoldService, RedisSeatHoldService>();
            return;
        }

        builder.Services.AddScoped<IOtpCache, NoOpOtpCache>();
        builder.Services.AddScoped<IBoatHoldService, NoOpBoatHoldService>();
        builder.Services.AddScoped<IPaymentProcessingLock, NoOpPaymentProcessingLock>();
        builder.Services.AddSingleton<ISeatHoldService, InMemorySeatHoldService>();
    }
}
