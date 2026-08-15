using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

/// <summary>
/// Shared parse fallback cho các enum lưu dạng string trong DB.
/// Trả về giá trị mặc định nếu DB có dữ liệu cũ / lệch enum, tránh
/// InvalidOperationException "Cannot convert string value to enum" làm crash
/// mọi query load entity tương ứng.
/// </summary>
internal static class BoatConfigurationFallbacks
{
    public static SeatSetupType ParseSeatSetupType(string value) =>
        Enum.TryParse<SeatSetupType>(value, ignoreCase: true, out var result)
            ? result
            : SeatSetupType.FullStandard;
}
