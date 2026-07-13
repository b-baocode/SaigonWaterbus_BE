using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Promotions;

/// <summary>
/// Trạng thái thực tế của khuyến mãi tính lúc đọc, gộp trạng thái đã lưu với hạn
/// dùng và lượt/ngân sách đã tiêu. Không lưu DB — luôn suy ra để khỏi cần cron
/// cập nhật Scheduled → Running → Ended.
/// </summary>
public static class PromotionEffectiveState
{
    public const string Draft = "Draft";
    public const string Scheduled = "Scheduled";
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Exhausted = "Exhausted";
    public const string Ended = "Ended";
    public const string Archived = "Archived";

    public static string Compute(
        PromotionStatus status,
        DateTimeOffset validFrom,
        DateTimeOffset validTo,
        DateTimeOffset now,
        int totalUsed,
        int? usageLimit,
        decimal budgetSpent,
        decimal? budgetCap)
    {
        switch (status)
        {
            case PromotionStatus.Draft:
                return Draft;
            case PromotionStatus.Paused:
                return Paused;
            case PromotionStatus.Archived:
                return Archived;
        }

        if (now < validFrom)
        {
            return Scheduled;
        }

        if (now > validTo)
        {
            return Ended;
        }

        if ((usageLimit.HasValue && totalUsed >= usageLimit.Value)
            || (budgetCap.HasValue && budgetSpent >= budgetCap.Value))
        {
            return Exhausted;
        }

        return Running;
    }
}
