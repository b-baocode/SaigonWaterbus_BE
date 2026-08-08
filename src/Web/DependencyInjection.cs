using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Auth;
using SaigonWaterbus.Infrastructure.Data;
using SaigonWaterbus.Web.Infrastructure;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    private static readonly string[] DefaultCorsAllowedOrigins =
    [
        "http://localhost:3000",
        "http://localhost:5173",
        "http://localhost:5174",
        "http://localhost:5177",
        "https://localhost:3000",
        "https://localhost:5173",
        "https://localhost:5174",
        "https://localhost:5177",
        "https://waterbus.top",
        "https://www.waterbus.top"
    ];

    /// <summary>Khóa phân vùng rate limit: IP client thật (X-Forwarded-For sau proxy Azure).</summary>
    private static string ResolveClientKey(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        var allowedOrigins = ResolveAllowedOrigins(builder.Configuration);

        builder.Services.AddDatabaseDeveloperPageExceptionFilter();
        builder.Services.AddMemoryCache();
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
            [
                "application/json",
                "application/problem+json",
                "application/vnd.api+json"
            ]);
        });
        builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
            options.Level = System.IO.Compression.CompressionLevel.Fastest);
        builder.Services.Configure<GzipCompressionProviderOptions>(options =>
            options.Level = System.IO.Compression.CompressionLevel.Fastest);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IEndpointResponseCache, RedisBackedEndpointResponseCache>();
        builder.Services.AddScoped<IUserContext, CurrentUser>();
        builder.Services.AddScoped<IClientInfoProvider, CurrentClientInfo>();
        builder.Services.AddScoped<ICharterBookingTicketPdfRenderer, QuestPdfCharterBookingTicketPdfRenderer>();
        builder.Services.AddScoped<IBookingTicketPdfRenderer, QuestPdfBookingTicketPdfRenderer>();

        builder.Services.AddSignalR();
        builder.Services.AddSingleton<ITripSeatNotifier, SignalRTripSeatNotifier>();
        builder.Services.AddSingleton<ICharterBookingRealtimeNotifier, SignalRCharterBookingRealtimeNotifier>();
        builder.Services.AddSingleton<IIncidentRealtimeNotifier, SignalRIncidentRealtimeNotifier>();
        builder.Services.AddSingleton<ITripDelayRealtimeNotifier, SignalRTripDelayRealtimeNotifier>();
        builder.Services.AddSingleton<INotificationRealtimeNotifier, SignalRNotificationRealtimeNotifier>();

        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new DateOnlyJsonConverter());
            options.SerializerOptions.Converters.Add(new NullableDateOnlyJsonConverter());
            options.SerializerOptions.Converters.Add(new TimeOnlyJsonConverter());
            options.SerializerOptions.Converters.Add(new NullableTimeOnlyJsonConverter());
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        });

        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
                var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken)
                            && path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorization();

        // Rate limit cho endpoint tốn kém (chatbox gọi LLM). Chặn spam/lạm dụng chi phí.
        // Phân vùng theo IP client (ưu tiên X-Forwarded-For vì sau reverse proxy của Azure).
        var assistantPermit = builder.Configuration.GetValue<int?>("RateLimiting:AssistantChatPerWindow") ?? 8;
        var assistantWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:AssistantChatWindowSeconds") ?? 60;

        // Hướng dẫn viên bằng giọng nói: mỗi lượt tốn STT + LLM + TTS (chatbox chỉ tốn LLM),
        // nên siết chặt hơn.
        var tourGuideVoicePermit = builder.Configuration.GetValue<int?>("RateLimiting:TourGuideVoicePerWindow") ?? 5;
        var tourGuideVoiceWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:TourGuideVoiceWindowSeconds") ?? 60;

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(SaigonWaterbus.Web.Endpoints.Assistant.RateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = assistantPermit,
                        Window = TimeSpan.FromSeconds(assistantWindowSeconds),
                        QueueLimit = 0,
                    }));
            options.AddPolicy(SaigonWaterbus.Web.Endpoints.TourGuideVoice.RateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = tourGuideVoicePermit,
                        Window = TimeSpan.FromSeconds(tourGuideVoiceWindowSeconds),
                        QueueLimit = 0,
                    }));
        });

        builder.Services.AddSwaggerGen(options =>
        {
            options.SchemaFilter<StringEnumSchemaFilter>();
            options.SchemaFilter<TimeOnlySchemaFilter>();
            options.DocumentFilter<TimeOnlyDocumentFilter>();

            options.MapType<SeatSetupType>(() => new OpenApiSchema
            {
                Type = "string",
                Enum =
                [
                    new Microsoft.OpenApi.Any.OpenApiString(nameof(SeatSetupType.FullStandard)),
                    new Microsoft.OpenApi.Any.OpenApiString(nameof(SeatSetupType.StandardAndVip))
                ],
                Example = new Microsoft.OpenApi.Any.OpenApiString(nameof(SeatSetupType.FullStandard))
            });

            options.MapType<SeatSetupType?>(() => new OpenApiSchema
            {
                Type = "string",
                Nullable = true,
                Enum =
                [
                    new Microsoft.OpenApi.Any.OpenApiString(nameof(SeatSetupType.FullStandard)),
                    new Microsoft.OpenApi.Any.OpenApiString(nameof(SeatSetupType.StandardAndVip))
                ]
            });

            options.MapType<DateOnly>(() => new OpenApiSchema
            {
                Type = "string",
                Example = new Microsoft.OpenApi.Any.OpenApiString("10/06/2026")
            });

            options.MapType<DateOnly?>(() => new OpenApiSchema
            {
                Type = "string",
                Nullable = true,
                Example = new Microsoft.OpenApi.Any.OpenApiString("10/06/2026")
            });

            options.MapType<TimeOnly>(() => new OpenApiSchema
            {
                Type = "string",
                Example = new Microsoft.OpenApi.Any.OpenApiString("06:00")
            });

            options.MapType<TimeOnly?>(() => new OpenApiSchema
            {
                Type = "string",
                Nullable = true,
                Example = new Microsoft.OpenApi.Any.OpenApiString("06:00")
            });

            options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = JwtBearerDefaults.AuthenticationScheme,
                BearerFormat = "JWT",
                Description = "Enter a valid bearer token."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = JwtBearerDefaults.AuthenticationScheme
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        builder.Services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<ApiExceptionOperationTransformer>();
        });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("FrontendClientPolicy", corsBuilder =>
            {
                corsBuilder
                    .WithOrigins(allowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    // SignalR (websocket negotiate) yêu cầu credentials khi origin cụ thể.
                    .AllowCredentials();
            });
        });
    }

    private static string[] ResolveAllowedOrigins(IConfiguration configuration)
    {
        var origins = new List<string>();
        var section = configuration.GetSection("Cors:AllowedOrigins");
        var configuredOrigins = section.Get<string[]>();
        if (configuredOrigins is not null)
        {
            origins.AddRange(configuredOrigins);
        }

        origins.AddRange(SplitOrigins(section.Value));
        origins.AddRange(SplitOrigins(configuration["Cors:AdditionalAllowedOrigins"]));
        origins.AddRange(DefaultCorsAllowedOrigins);

        return origins
            .Select(NormalizeOrigin)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> SplitOrigins(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeOrigin(string origin)
    {
        var trimmed = origin.Trim().TrimEnd('/');
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : trimmed;
    }

    public static void AddKeyVaultIfConfigured(this IHostApplicationBuilder builder)
    {
        var keyVaultUri = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(keyVaultUri),
                new DefaultAzureCredential());
        }
    }
}
