using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Data;
using SaigonWaterbus.Infrastructure.Data.Interceptors;
using SaigonWaterbus.Infrastructure.Notifications;
using SaigonWaterbus.Infrastructure.Options;
using SaigonWaterbus.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(Services.Database);
        Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");

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
        builder.Services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        builder.Services.AddScoped<IRegistrationNotificationService, GmailRegistrationNotificationService>();

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();
        builder.Services.Configure<DatabaseStartupSettings>(options =>
        {
            options.ResetOnStartup = builder.Environment.IsDevelopment() &&
                builder.Configuration.GetValue<bool>("Database:ResetOnStartup");
        });
        builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));

        builder.Services.AddSingleton(TimeProvider.System);
    }
}
