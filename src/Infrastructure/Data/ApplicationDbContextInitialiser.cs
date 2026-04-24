using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data;

public static class InitialiserExtensions
{
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

        await initialiser.InitialiseAsync();
        await initialiser.SeedAsync();
    }
}

public class ApplicationDbContextInitialiser
{
    private readonly ILogger<ApplicationDbContextInitialiser> _logger;
    private readonly ApplicationDbContext _context;

    public ApplicationDbContextInitialiser(ILogger<ApplicationDbContextInitialiser> logger, ApplicationDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            // Apply migrations when available. For initial setup without migrations,
            // create schema without destroying existing data.
            if (_context.Database.GetMigrations().Any())
            {
                await _context.Database.MigrateAsync();
            }
            else
            {
                await _context.Database.EnsureCreatedAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }

    public async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    public async Task TrySeedAsync()
    {
        var existingRoleCodes = await _context.Roles
            .Select(x => x.Code)
            .ToListAsync();

        var missingRoles = Roles.BuiltIn
            .Where(x => !existingRoleCodes.Contains(x.Code))
            .Select(x => new Role
            {
                Code = x.Code,
                SystemName = x.SystemName,
                DisplayName = x.DisplayName,
                Description = x.Description,
                DefaultScopeType = x.DefaultScopeType,
                IsSystem = true
            })
            .ToList();

        if (missingRoles.Count == 0)
        {
            return;
        }

        _context.Roles.AddRange(missingRoles);
        await _context.SaveChangesAsync();
    }
}
