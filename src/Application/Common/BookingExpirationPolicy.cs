namespace SaigonWaterbus.Application.Common;

public static class BookingExpirationPolicy
{
    public static TimeSpan PaymentLinkTtl => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Ngừng NHẬN ĐƠN MỚI cho tuyến thường trước giờ tàu rời BẾN KHÁCH LÊN khoảng này (xem
    /// BookingCutoffSupport — không phải giờ rời bến đầu tuyến). Áp ở cả tạo booking,
    /// giữ ghế và tìm chuyến.
    ///
    /// Mốc này KHÔNG cắt hạn giữ chỗ của đơn đã tạo: HoldExpiresAt bị chặn trên bởi chính giờ
    /// tàu rời bến khách lên (xem BookingLegResolver.ResolveHoldExpiresAt). Nhờ vậy khách đặt
    /// ngay sát mốc vẫn còn trọn khoảng này để thanh toán, mà ghế vẫn nhả trước khi tàu chạy.
    /// </summary>
    public static TimeSpan BookingCutoffBeforeDeparture => TimeSpan.FromMinutes(3);

    public static TimeSpan CharterQuoteResponseTtl => TimeSpan.FromHours(2);

    public static TimeSpan CharterPaymentCompletionTtl => TimeSpan.FromHours(12);
}
