using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Ai;
using SaigonWaterbus.Infrastructure.Auth;
using SaigonWaterbus.Infrastructure.Data;
using SaigonWaterbus.Infrastructure.Data.Interceptors;
using SaigonWaterbus.Infrastructure.Incidents;
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
    private const string IncidentGpsHookHttpClientName = "IncidentGpsHook";

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
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
            options.ConfigureWarnings(warnings =>
            {
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning);
                warnings.Ignore(RelationalEventId.MultipleCollectionIncludeWarning);
            });
        });

        builder.EnrichNpgsqlDbContext<ApplicationDbContext>();

        builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        builder.Services.AddSingleton<IDatabaseExceptionClassifier, NpgsqlDatabaseExceptionClassifier>();
        builder.Services.AddScoped<IDatabaseMigrationInspector, DatabaseMigrationInspector>();
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
        builder.Services.AddScoped<IIncidentGpsHookNotifier, HttpIncidentGpsHookNotifier>();
        builder.Services.AddScoped<IProfileImageStorageService, CloudinaryProfileImageStorageService>();
        builder.Services.AddScoped<IBlogImageStorageService, CloudinaryBlogImageStorageService>();
        builder.Services.AddScoped<IBoatImageStorageService, CloudinaryBoatImageStorageService>();
        builder.Services.AddScoped<IBoatDocumentStorageService, CloudinaryBoatDocumentStorageService>();
        builder.Services.AddScoped<IStationImageStorageService, CloudinaryStationImageStorageService>();
        builder.Services.AddScoped<IPromotionImageStorageService, CloudinaryPromotionImageStorageService>();
        builder.Services.AddScoped<IPromotionLock, PromotionLock>();
        builder.Services.AddHttpClient(BrevoHttpClientName);
        builder.Services.AddHttpClient(EsmsHttpClientName);
        builder.Services.AddHttpClient(PayOsHttpClientName);
        builder.Services.AddHttpClient(GeminiChatCompletionService.HttpClientName);
        builder.Services.AddScoped<IChatCompletionService, GeminiChatCompletionService>();
        // Singleton: chỉ giữ đường dẫn thư mục, không giữ nội dung prompt (đọc lại file mỗi lượt).
        builder.Services.AddSingleton<IAssistantPromptStore, FileAssistantPromptStore>();
        // Singleton: GoogleCredential tự cache và tự làm mới access token bên trong nó.
        builder.Services.AddSingleton<GoogleCloudCredentials>();
        builder.Services.AddHttpClient(GoogleTextToSpeechService.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<GoogleTextToSpeechOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        });
        builder.Services.AddScoped<ITextToSpeechService, GoogleTextToSpeechService>();
        AddSpeechToText(builder);
        builder.Services.AddHttpClient(IncidentGpsHookHttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<IncidentGpsHookOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        });
        AddRedis(builder);
        AddRedisBackedServices(builder);
        builder.Services.AddHostedService<BookingHoldExpiryService>();
        builder.Services.AddHostedService<RefundReleaseExpiryService>();
        builder.Services.AddHostedService<TripReminderService>();
        builder.Services.AddHostedService<StaffOperationalNotificationService>();
        builder.Services.AddHostedService<SightseeingTripAutoCancellationService>();
        builder.Services.AddHostedService<CharterTripExpirationHostedService>();
        builder.Services.AddHostedService<PaymentPendingExpirationHostedService>();
        builder.Services.AddHostedService<ChatConversationLifecycleService>();
        builder.Services.AddHostedService<CharterBookingTicketReconciliationHostedService>();
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
        builder.Services.Configure<TripStatusAutoSyncOptions>(builder.Configuration.GetSection(TripStatusAutoSyncOptions.SectionName));
        builder.Services.Configure<CharterBookingExpirationOptions>(builder.Configuration.GetSection(CharterBookingExpirationOptions.SectionName));
        builder.Services.Configure<CharterBookingTicketReconciliationOptions>(
            builder.Configuration.GetSection(CharterBookingTicketReconciliationOptions.SectionName));
        builder.Services.Configure<IncidentGpsHookOptions>(options =>
        {
            builder.Configuration.GetSection(IncidentGpsHookOptions.SectionName).Bind(options);
            if (string.IsNullOrWhiteSpace(options.Secret))
            {
                options.Secret = builder.Configuration["LIVE_HOOK_SECRET"] ?? string.Empty;
            }
        });
        builder.Services.Configure<CharterRouteEstimateOptions>(builder.Configuration.GetSection(CharterRouteEstimateOptions.SectionName));
        builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
        builder.Services.Configure<AssistantPromptOptions>(
            builder.Configuration.GetSection(AssistantPromptOptions.SectionName));
        builder.Services.Configure<GoogleTextToSpeechOptions>(
            builder.Configuration.GetSection(GoogleTextToSpeechOptions.SectionName));
        builder.Services.Configure<GoogleCloudSpeechToTextOptions>(
            builder.Configuration.GetSection(GoogleCloudSpeechToTextOptions.SectionName));
        builder.Services.Configure<GoogleCloudCredentialsOptions>(
            builder.Configuration.GetSection(GoogleCloudCredentialsOptions.SectionName));
        CharterRouteEstimateOptionsSetup.ConfigureDefaults(
            builder.Configuration.GetSection(CharterRouteEstimateOptions.SectionName).Get<CharterRouteEstimateOptions>());

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHostedService<CharterBookingExpirationHostedService>();
        builder.Services.AddHostedService<TicketExpirationHostedService>();
        builder.Services.AddHostedService<TripStatusAutoSyncService>();
    }

    /// <summary>
    /// Chọn provider chép lời theo cấu hình "SpeechToText:Provider".
    ///
    /// Mặc định là Gemini vì nó chạy được NGAY với key sẵn có, nhưng đo thật cho thấy nó mất
    /// 3.5–4.5s/lượt — nên khi có key Google Cloud thì đổi sang "GoogleCloud" (nhanh hơn hẳn
    /// vì là dịch vụ chuyên chép lời). Đổi provider chỉ là đổi một dòng config, không sửa code:
    /// đó là điểm của ISpeechToTextService.
    /// </summary>
    private static void AddSpeechToText(IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["SpeechToText:Provider"];

        if (string.Equals(provider, "GoogleCloud", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddHttpClient(GoogleCloudSpeechToTextService.HttpClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<GoogleCloudSpeechToTextOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
            });
            builder.Services.AddScoped<ISpeechToTextService, GoogleCloudSpeechToTextService>();
            return;
        }

        // HttpClient "Gemini" đã đăng ký ở trên — dùng chung để giữ nguyên cấu hình proxy.
        builder.Services.AddScoped<ISpeechToTextService, GeminiSpeechToTextService>();
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
            var configurationOptions = CreateRedisConfigurationOptions(redisOptions.ConnectionString);
            configurationOptions.AbortOnConnectFail = false;
            configurationOptions.ClientName = string.IsNullOrWhiteSpace(configurationOptions.ClientName)
                ? "saigon-waterbus-api"
                : configurationOptions.ClientName;

            return ConnectionMultiplexer.Connect(configurationOptions);
        });
    }

    private static ConfigurationOptions CreateRedisConfigurationOptions(string connectionString)
    {
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "redis" && uri.Scheme != "rediss"))
        {
            return ConfigurationOptions.Parse(connectionString);
        }

        var options = new ConfigurationOptions
        {
            Ssl = uri.Scheme == "rediss"
        };

        options.EndPoints.Add(uri.Host, uri.Port > 0 ? uri.Port : 6379);

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            if (parts.Length == 2)
            {
                options.User = WebUtility.UrlDecode(parts[0]);
                options.Password = WebUtility.UrlDecode(parts[1]);
            }
            else
            {
                options.Password = WebUtility.UrlDecode(parts[0]);
            }
        }

        var databasePath = uri.AbsolutePath.Trim('/');
        if (int.TryParse(databasePath, out var database))
        {
            options.DefaultDatabase = database;
        }

        return options;
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
