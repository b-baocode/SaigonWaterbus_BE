namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>Một migration và trạng thái của nó trên database đang kết nối.</summary>
/// <param name="MigrationId">Tên đầy đủ, ví dụ 20260819104500_AddCharterBookingAutoDeleteTrigger.</param>
/// <param name="Name">Phần tên sau timestamp, ví dụ AddCharterBookingAutoDeleteTrigger.</param>
/// <param name="StampedAt">Timestamp ở đầu MigrationId — thời điểm migration được TẠO RA, không phải lúc chạy.</param>
/// <param name="IsApplied">Đã có trong __EFMigrationsHistory của database đang kết nối hay chưa.</param>
public sealed record DatabaseMigrationDto(
    string MigrationId,
    string Name,
    DateTimeOffset? StampedAt,
    bool IsApplied);

/// <summary>
/// Trạng thái migration của database đang kết nối. PendingCount &gt; 0 nghĩa là schema thật đang
/// cũ hơn code — thường gặp vì migration được chạy tay chứ không tự động khi deploy.
/// </summary>
public sealed record DatabaseMigrationStatusDto(
    string DatabaseName,
    string ServerVersion,
    int TotalCount,
    int AppliedCount,
    int PendingCount,
    IReadOnlyList<DatabaseMigrationDto> Migrations);

public interface IDatabaseMigrationInspector
{
    Task<DatabaseMigrationStatusDto> GetStatusAsync(CancellationToken cancellationToken);
}
