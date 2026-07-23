namespace SaigonWaterbus.Application.Common;

public static class BookingExpirationPolicy
{
    public static TimeSpan PaymentLinkTtl => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Ngừng bán vé tuyến thường trước giờ tàu rời BẾN KHÁCH LÊN khoảng này (xem
    /// BookingCutoffSupport — không phải giờ rời bến đầu tuyến). Áp ở cả tạo booking,
    /// giữ ghế và tìm chuyến.
    ///
    /// Lưu ý: thời hạn giữ chỗ chờ thanh toán là tối đa 15 phút, nhưng khi đặt sát giờ thì
    /// HoldExpiresAt phải bị cắt về mốc đóng bán này để ghế không bị giữ sau giờ lên tàu.
    /// </summary>
    public static TimeSpan BookingCutoffBeforeDeparture => TimeSpan.FromMinutes(10);

    public static TimeSpan CharterQuoteResponseTtl => TimeSpan.FromHours(2);

    public static TimeSpan CharterPaymentCompletionTtl => TimeSpan.FromHours(12);
}
