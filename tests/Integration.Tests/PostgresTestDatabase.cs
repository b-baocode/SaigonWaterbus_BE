using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaigonWaterbus.Infrastructure.Data;

namespace SaigonWaterbus.Integration.Tests;

/// <summary>
/// Một database Postgres dùng một lần cho mỗi test: tạo mới → chạy migration → xoá khi xong.
/// KHÔNG bao giờ đụng tới database của ứng dụng — mọi database do lớp này tạo đều mang tiền tố
/// <see cref="DatabaseNamePrefix"/>, và bước xoá từ chối chạy nếu tên không khớp tiền tố đó.
///
/// Connection string lấy từ biến môi trường WATERBUS_TEST_DB (CI trỏ vào service container),
/// mặc định là Postgres localhost của máy dev. Database trong chuỗi đó chỉ dùng để kết nối
/// quản trị (CREATE/DROP DATABASE), không bị ghi vào.
/// </summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    public const string DatabaseNamePrefix = "waterbus_test_";

    private const string DefaultAdminConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=12345;";

    private readonly string _adminConnectionString;

    private PostgresTestDatabase(string adminConnectionString, string connectionString, string databaseName)
    {
        _adminConnectionString = adminConnectionString;
        ConnectionString = connectionString;
        DatabaseName = databaseName;
    }

    public string ConnectionString { get; }

    public string DatabaseName { get; }

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("WATERBUS_TEST_DB")
            ?? DefaultAdminConnectionString;
        var databaseName = DatabaseNamePrefix + Guid.NewGuid().ToString("N")[..12];

        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""CREATE DATABASE "{databaseName}" """, connection);
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName
        }.ConnectionString;

        var database = new PostgresTestDatabase(adminConnectionString, connectionString, databaseName);

        // Chạy migration thật thay vì EnsureCreated: test chạy trên đúng schema mà deploy sẽ tạo ra,
        // và một chuỗi migration hỏng sẽ làm test đỏ ngay tại đây.
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        return database;
    }

    public ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseNetTopologySuite())
            .Options);

    public async ValueTask DisposeAsync()
    {
        // Chốt an toàn: không bao giờ DROP một database không phải do lớp này tạo ra.
        if (!DatabaseName.StartsWith(DatabaseNamePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Từ chối xoá database '{DatabaseName}' vì không mang tiền tố '{DatabaseNamePrefix}'.");
        }

        // Kết nối còn trong pool sẽ chặn DROP DATABASE.
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""DROP DATABASE IF EXISTS "{DatabaseName}" WITH (FORCE)""", connection);
        await command.ExecuteNonQueryAsync();
    }
}
