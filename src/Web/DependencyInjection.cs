using System.Text;
using System.Text.Json.Serialization;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Auth;
using SaigonWaterbus.Infrastructure.Data;
using SaigonWaterbus.Web.Infrastructure;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? [
                "http://localhost:3000",
                "http://localhost:5173",
                "https://localhost:3000",
                "https://localhost:5173"
            ];

        builder.Services.AddDatabaseDeveloperPageExceptionFilter();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IUserContext, CurrentUser>();
        builder.Services.AddScoped<IClientInfoProvider, CurrentClientInfo>();

        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new DateOnlyJsonConverter());
            options.SerializerOptions.Converters.Add(new NullableDateOnlyJsonConverter());
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        });

        // Customise default API behaviour
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
            });

        builder.Services.AddAuthorization();

        builder.Services.AddSwaggerGen(options =>
        {
            options.SchemaFilter<StringEnumSchemaFilter>();

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
                    .AllowAnyHeader();
            });
        });
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
