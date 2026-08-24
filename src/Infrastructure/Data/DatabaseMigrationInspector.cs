using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Data;

/// <summary>
/// Đọc trạng thái migration từ chính database đang kết nối: đối chiếu danh sách migration biên dịch
/// trong assembly với bảng __EFMigrationsHistory. Chỉ đọc, không chạy migration nào.
/// </summary>
public sealed class DatabaseMigrationInspector : IDatabaseMigrationInspector
{
    private const string MigrationIdTimestampFormat = "yyyyMMddHHmmss";
    private const int MigrationIdTimestampLength = 14;

    private readonly ApplicationDbContext _context;

    public DatabaseMigrationInspector(ApplicationDbContext context) => _context = context;

    public async Task<DatabaseMigrationStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var applied = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToHashSet();
        var all = _context.Database.GetMigrations().ToList();

        // Migration đã chạy trên DB nhưng không còn trong code (nhánh khác, hoặc file bị xoá) vẫn
        // phải hiện ra — đó chính là dấu hiệu schema và code lệch nhau.
        var migrationIds = all.Union(applied).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var migrations = migrationIds
            .Select(id => new DatabaseMigrationDto(
                id,
                ResolveName(id),
                ResolveStampedAt(id),
                applied.Contains(id)))
            .ToList();

        return new DatabaseMigrationStatusDto(
            _context.Database.GetDbConnection().Database,
            await ResolveServerVersionAsync(cancellationToken),
            migrations.Count,
            migrations.Count(x => x.IsApplied),
            migrations.Count(x => !x.IsApplied),
            migrations);
    }

    private static string ResolveName(string migrationId) =>
        migrationId.Length > MigrationIdTimestampLength + 1
            ? migrationId[(MigrationIdTimestampLength + 1)..]
            : migrationId;

    private static DateTimeOffset? ResolveStampedAt(string migrationId) =>
        migrationId.Length >= MigrationIdTimestampLength
        && DateTime.TryParseExact(
            migrationId[..MigrationIdTimestampLength],
            MigrationIdTimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var stampedAt)
            ? new DateTimeOffset(stampedAt, TimeSpan.Zero)
            : null;

    private async Task<string> ResolveServerVersionAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await _context.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            return connection.ServerVersion;
        }
        finally
        {
            if (openedHere)
            {
                await _context.Database.CloseConnectionAsync();
            }
        }
    }
}
