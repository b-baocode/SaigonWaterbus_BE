namespace SaigonWaterbus.Application.Common;

public static class BookingExpirationPolicy
{
    public static TimeSpan PaymentLinkTtl => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Ngừng bán vé tuyến thường trước giờ tàu rời BẾN KHÁCH LÊN khoảng này (xem
    /// BookingCutoffSupport — không phải giờ rời bến đầu tuyến). Áp ở cả tạo booking,
    /// giữ ghế và tìm chuyến.
    ///
    /// Lưu ý: mốc này NGẮN HƠN thời hạn giữ chỗ chờ thanh toán (15 phút), nên booking
    /// đặt sát giờ có thể còn PendingPayment khi tàu đã rời bến lên. Ghế vẫn bị giữ tới hết 15
    /// phút rồi mới nhả — lúc đó bán lại không còn ý nghĩa cho chặng đã qua.
    /// </summary>
    public static TimeSpan BookingCutoffBeforeDeparture => TimeSpan.FromMinutes(10);

    public static TimeSpan CharterQuoteResponseTtl => TimeSpan.FromHours(2);

    public static TimeSpan CharterPaymentCompletionTtl => TimeSpan.FromHours(12);
}
