using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SaigonWaterbus.Infrastructure.Data;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string ConnectionStringName = "SaigonWaterbusDb";
    private const string FallbackConnectionString = "Host=localhost;Port=5432;Database=SaigonWaterbusDb;Username=postgres;";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable($"ConnectionStrings__{ConnectionStringName}")
            ?? FallbackConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
