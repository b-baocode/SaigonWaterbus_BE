namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>
/// Đồng bộ bảng read-model operation_schedule_entries từ Trips + CustomBookingRequests
/// trong cửa sổ thời gian [from, to). Idempotent: gọi lại nhiều lần cho cùng dữ liệu là an toàn.
/// </summary>
public interface IOperationScheduleSynchronizer
{
    /// <summary>Đồng bộ và trả về số nguồn (trip + custom booking) đã xử lý trong cửa sổ.</summary>
    Task<int> SyncAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
