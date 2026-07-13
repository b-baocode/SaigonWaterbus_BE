using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>
/// Nạp một promotion theo mã kèm khóa hàng (SELECT ... FOR UPDATE) để mọi kiểm
/// tra lượt/ngân sách/lượt-mỗi-tài-khoản chạy tuần tự dưới cùng một transaction,
/// tránh race khi nhiều đơn cùng dùng một mã (vd flash sale). Phải được gọi bên
/// trong một transaction đang mở. Trả về null nếu không tìm thấy mã.
/// </summary>
public interface IPromotionLock
{
    Task<Promotion?> AcquireByCodeAsync(string normalizedCode, CancellationToken cancellationToken);
}
