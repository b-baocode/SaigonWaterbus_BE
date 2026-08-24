using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Maintenance;

/// <summary>
/// Liệt kê migration của database đang kết nối: cái nào đã chạy, cái nào còn treo. Dùng để đối
/// chiếu schema thật với code — migration ở dự án này chạy tay nên hai bên rất dễ lệch nhau.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record GetDatabaseMigrationsQuery : IRequest<DatabaseMigrationStatusDto>;

public sealed class GetDatabaseMigrationsQueryHandler
    : IRequestHandler<GetDatabaseMigrationsQuery, DatabaseMigrationStatusDto>
{
    private readonly IDatabaseMigrationInspector _inspector;

    public GetDatabaseMigrationsQueryHandler(IDatabaseMigrationInspector inspector) =>
        _inspector = inspector;

    public Task<DatabaseMigrationStatusDto> Handle(
        GetDatabaseMigrationsQuery request,
        CancellationToken cancellationToken) =>
        _inspector.GetStatusAsync(cancellationToken);
}
