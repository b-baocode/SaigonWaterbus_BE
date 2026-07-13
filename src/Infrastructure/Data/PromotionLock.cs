using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data;

/// <summary>
/// Nạp promotion kèm khóa hàng bằng SELECT ... FOR UPDATE (Postgres). Phải chạy
/// trong transaction đang mở; khóa được giữ tới khi transaction kết thúc, buộc các
/// đơn cùng dùng một mã phải kiểm tra lượt/ngân sách tuần tự.
/// </summary>
internal sealed class PromotionLock : IPromotionLock
{
    private readonly ApplicationDbContext _context;

    public PromotionLock(ApplicationDbContext context) => _context = context;

    public async Task<Promotion?> AcquireByCodeAsync(string normalizedCode, CancellationToken cancellationToken)
    {
        return await _context.Set<Promotion>()
            .FromSql($"SELECT * FROM promotions WHERE promotion_code = {normalizedCode} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
    }
}
