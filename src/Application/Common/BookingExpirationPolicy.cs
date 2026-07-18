namespace SaigonWaterbus.Application.Common;

public static class BookingExpirationPolicy
{
    public static TimeSpan PaymentLinkTtl => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Ngừng bán vé tuyến thường trước giờ khởi hành khoảng này — đủ để khách kịp thanh toán
    /// (giữ chỗ 15 phút) và tới bến/soát vé. Áp ở cả tạo booking, giữ ghế và tìm chuyến.
    /// </summary>
    public static TimeSpan BookingCutoffBeforeDeparture => TimeSpan.FromMinutes(20);

    public static TimeSpan CharterQuoteResponseTtl => TimeSpan.FromHours(2);

    public static TimeSpan CharterPaymentCompletionTtl => TimeSpan.FromHours(12);
}
